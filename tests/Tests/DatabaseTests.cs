using System.IO;
using AegisMouse.Core;
using AegisMouse.Database;
using Xunit;

namespace AegisMouse.Tests;

public class DatabaseTests : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aegismouse-test-{Guid.NewGuid():N}.db");
    readonly MacroDatabase _db;

    public DatabaseTests() => _db = new MacroDatabase(_dbPath);

    [Fact]
    public void SaveLoadDelete_Profile()
    {
        var p = new GameProfile { Name = "Valorant" };
        p.Bindings.Add(new ButtonBinding { Trigger = MouseButton.Side1, Mode = BindingMode.BurstFire, BurstCount = 5 });
        _db.SaveProfile(p);

        var loaded = Assert.Single(_db.GetProfiles());
        Assert.Equal("Valorant", loaded.Name);
        Assert.Equal(BindingMode.BurstFire, loaded.Bindings[0].Mode);
        Assert.Equal(5, loaded.Bindings[0].BurstCount);

        _db.DeleteProfile(p.Id);
        Assert.Empty(_db.GetProfiles());
    }

    [Fact]
    public void UpdateProfile_Overwrites()
    {
        var p = new GameProfile { Name = "Apex" };
        _db.SaveProfile(p);
        p.Name = "Apex (updated)";
        _db.SaveProfile(p);
        var loaded = Assert.Single(_db.GetProfiles());
        Assert.Equal("Apex (updated)", loaded.Name);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        Assert.Null(_db.GetSetting("nope"));
        _db.SetSetting("last_profile", "abc");
        Assert.Equal("abc", _db.GetSetting("last_profile"));
        _db.SetSetting("last_profile", "def");
        Assert.Equal("def", _db.GetSetting("last_profile"));
    }

    [Fact]
    public async Task Concurrent_Saves_Are_Safe()
    {
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            try
            {
                var p = new GameProfile { Name = $"P{i}" };
                _db.SaveProfile(p);
                _db.SaveProfile(p);
                _db.GetProfiles();
                _db.SetSetting($"k{i}", "v");
                Assert.NotNull(_db.GetSetting($"k{i}"));
            }
            catch (Exception ex) { errors.Add(ex); }
        }));
        await Task.WhenAll(tasks);
        Assert.Empty(errors);
        Assert.Equal(16, _db.GetProfiles().Count);
    }

    public void Dispose() => _db.Dispose();
}