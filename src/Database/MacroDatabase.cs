using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AegisMouse.Core;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AegisMouse.Database;

/// <summary>SQLite persistence for game profiles and app settings (JSON bodies).</summary>
public sealed class MacroDatabase : IDisposable
{
    public string DatabasePath { get; }
    readonly SqliteConnection _conn;
    readonly object _sync = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public MacroDatabase(string? path = null)
    {
        var dir = path is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AegisMouse", "data") : Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        DatabasePath = path ?? Path.Combine(dir, "aegismouse.db");
        _conn = new SqliteConnection($"Data Source={DatabasePath}");
        _conn.Open();
        _conn.Execute("""
            CREATE TABLE IF NOT EXISTS profiles (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                body TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        Log.Information("AegisMouse database opened at {Path}", DatabasePath);
    }

    public List<GameProfile> GetProfiles()
    {
        lock (_sync)
        {
            var rows = _conn.Query<(string name, string body)>("SELECT name, body FROM profiles ORDER BY name").ToList();
            return rows.Select(r =>
            {
                try
                {
                    var p = JsonSerializer.Deserialize<GameProfile>(r.body, Json)!;
                    if (p is not null) ProfileSanitizer.Sanitize(p);
                    return p;
                }
                catch (Exception ex) { Log.Warning(ex, "Bad profile {Name}", r.name); return null; }
            }).Where(p => p is not null).Cast<GameProfile>().ToList();
        }
    }

    public void SaveProfile(GameProfile p)
    {
        ProfileSanitizer.Sanitize(p);
        lock (_sync)
        {
            _conn.Execute("""
                INSERT INTO profiles (id, name, body, created_utc, updated_utc) VALUES (@id, @name, @body, @created, @updated)
                ON CONFLICT(id) DO UPDATE SET name=@name, body=@body, updated_utc=@updated
                """, new
            {
                id = p.Id, name = p.Name, body = JsonSerializer.Serialize(p, Json),
                created = DateTime.UtcNow, updated = DateTime.UtcNow
            });
        }
    }

    public void DeleteProfile(string id)
    {
        lock (_sync)
            _conn.Execute("DELETE FROM profiles WHERE id=@id", new { id });
    }

    public string? GetSetting(string key)
    {
        lock (_sync)
            return _conn.QueryFirstOrDefault<string>("SELECT value FROM settings WHERE key=@key", new { key });
    }

    public void SetSetting(string key, string value)
    {
        lock (_sync)
            _conn.Execute("""
                INSERT INTO settings (key, value) VALUES (@key, @value)
                ON CONFLICT(key) DO UPDATE SET value=@value
                """, new { key, value });
    }

    public void Dispose() => _conn.Dispose();
}

public static class AppPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AegisMouse");

    public static string LogDirectory => Path.Combine(Root, "logs");
    public static string DatabasePath => Path.Combine(Root, "data", "aegismouse.db");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
    }

    public static void HardenStorage()
    {
        try
        {
            ApplyAcl(Root);
            ApplyAcl(LogDirectory);
            ApplyAcl(Path.GetDirectoryName(DatabasePath)!);
            foreach (var f in Directory.EnumerateFiles(Root)) ApplyAcl(f);
            foreach (var f in Directory.EnumerateFiles(LogDirectory)) ApplyAcl(f);
            foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(DatabasePath)!)) ApplyAcl(f);
            Log.Information("AegisMouse storage ACLs hardened");
        }
        catch (Exception ex) { Log.Warning(ex, "Storage hardening failed"); }
    }

    static void ApplyAcl(string path)
    {
        bool isDir = Directory.Exists(path);
        FileSystemSecurity sec = isDir
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        var inher = isDir
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        sec.SetAccessRuleProtection(true, false);
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inher, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inher, PropagationFlags.None, AccessControlType.Allow));
        if (WindowsIdentity.GetCurrent().User is { } self)
            sec.AddAccessRule(new FileSystemAccessRule(self, FileSystemRights.FullControl, inher, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory, inher, PropagationFlags.None, AccessControlType.Allow));
        if (isDir) new DirectoryInfo(path).SetAccessControl((DirectorySecurity)sec);
        else new FileInfo(path).SetAccessControl((FileSecurity)sec);
    }
}