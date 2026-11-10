using Gomoku.Core.Configuration;
using Xunit;

namespace Gomoku.Core.Tests.Configuration;

/// <summary>设置存储单元测试：保存加载往返、缺失与损坏文件回退默认值。</summary>
public class SettingsStoreTests
{
    private static string TempPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gomoku-settings-test");
        Directory.CreateDirectory(dir); // 保证写入前目录存在，避免测试顺序依赖导致偶发失败
        return Path.Combine(dir, $"{Guid.NewGuid():N}.json");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempPath();
        var settings = new AppSettings
        {
            AiDifficultyIndex = 7,
            BoardSkinIndex = 1,
            BoardMaterialIndex = 1,
            BackgroundIndex = 0,
            IsFullScreen = true,
            WindowLeft = 100,
            WindowTop = 50,
            WindowWidth = 1600,
            WindowHeight = 900,
        };

        SettingsStore.Save(settings, path);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(7, loaded.AiDifficultyIndex);
        Assert.Equal(1, loaded.BoardSkinIndex);
        Assert.Equal(1, loaded.BoardMaterialIndex);
        Assert.Equal(0, loaded.BackgroundIndex);
        Assert.True(loaded.IsFullScreen);
        Assert.Equal(100, loaded.WindowLeft);
        Assert.Equal(50, loaded.WindowTop);
        Assert.Equal(1600, loaded.WindowWidth);
        Assert.Equal(900, loaded.WindowHeight);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = SettingsStore.Load(TempPath());
        Assert.Equal(4, loaded.AiDifficultyIndex);   // 默认普通
        Assert.Equal(2, loaded.BoardSkinIndex);      // 默认动态视频
        Assert.False(loaded.IsFullScreen);
        Assert.Equal(1280, loaded.WindowWidth);
        Assert.Equal(800, loaded.WindowHeight);
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ not valid json !!!");
        var loaded = SettingsStore.Load(path);
        Assert.Equal(4, loaded.AiDifficultyIndex);
        Assert.Equal(2, loaded.BoardSkinIndex);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gomoku-settings-test", Guid.NewGuid().ToString("N"), "sub", "settings.json");
        SettingsStore.Save(new AppSettings(), path);
        Assert.True(File.Exists(path));
    }
}
