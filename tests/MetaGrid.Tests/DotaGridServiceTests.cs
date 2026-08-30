using System.Text.Json;
using System.Globalization;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using MetaGrid.Tests.TestSupport;

namespace MetaGrid.Tests;

public sealed class DotaGridServiceTests
{
    [Fact]
    public void ApplySnapshot_PreservesUnrelatedLayouts_AndReplacesManagedLayouts()
    {
        var service = new DotaGridService();
        var existing = new DotaHeroGridFile
        {
            Configs =
            [
                new DotaHeroGridLayout
                {
                    ConfigName = "Personal Layout",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Favorites",
                            XPosition = 0,
                            YPosition = 0,
                            Width = 100,
                            Height = 100,
                            HeroIds = [1, 2, 3]
                        }
                    ]
                },
                new DotaHeroGridLayout
                {
                    ConfigName = "MetaGrid - D2PT - Old Overview",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Carry",
                            XPosition = 0,
                            YPosition = 0,
                            Width = 100,
                            Height = 100,
                            HeroIds = [9]
                        }
                    ]
                }
            ]
        };

        var merged = service.ApplySnapshot(existing, StubProvider.CreateSnapshot([4, 5, 6]));

        Assert.Contains(merged.Configs, x => x.ConfigName == "Personal Layout");
        Assert.Single(merged.Configs.Where(x => x.ConfigName.StartsWith("MetaGrid - ", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ReadInstalledMetaGridHash_IsStable_ForUnchangedContent()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var service = new DotaGridService();
        var configPath = Path.Combine(paths.RootDirectory, "steam", "111", "570", "remote", "cfg", "hero_grid_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var file = service.ApplySnapshot(new DotaHeroGridFile(), StubProvider.CreateSnapshot([1, 2, 3]));
        await File.WriteAllTextAsync(configPath, service.Serialize(file));

        var hash1 = await service.ReadInstalledMetaGridHashAsync(configPath, CancellationToken.None);
        var hash2 = await service.ReadInstalledMetaGridHashAsync(configPath, CancellationToken.None);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task ReadInstalledMetaGridHash_Changes_WhenGridChanges()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var service = new DotaGridService();
        var configPath = Path.Combine(paths.RootDirectory, "steam", "111", "570", "remote", "cfg", "hero_grid_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var first = service.ApplySnapshot(new DotaHeroGridFile(), StubProvider.CreateSnapshot([1, 2, 3]));
        await File.WriteAllTextAsync(configPath, service.Serialize(first));
        var hash1 = await service.ReadInstalledMetaGridHashAsync(configPath, CancellationToken.None);

        var second = service.ApplySnapshot(new DotaHeroGridFile(), StubProvider.CreateSnapshot([1, 2, 3, 4]));
        await File.WriteAllTextAsync(configPath, service.Serialize(second));
        var hash2 = await service.ReadInstalledMetaGridHashAsync(configPath, CancellationToken.None);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task InspectInstalledMetaGridAsync_ReturnsMissingFile_WhenConfigDoesNotExist()
    {
        using var temp = new TemporaryDirectory();
        var service = new DotaGridService();

        var result = await service.InspectInstalledMetaGridAsync(Path.Combine(temp.Path, "missing.json"), CancellationToken.None);

        Assert.Equal(InstalledMetaGridState.MissingFile, result.State);
        Assert.Null(result.InstalledHash);
    }

    [Fact]
    public async Task InspectInstalledMetaGridAsync_ReturnsMalformedFile_WhenJsonCannotBeParsed()
    {
        using var temp = new TemporaryDirectory();
        var service = new DotaGridService();
        var configPath = Path.Combine(temp.Path, "hero_grid_config.json");
        await File.WriteAllTextAsync(configPath, "{ invalid json");

        var result = await service.InspectInstalledMetaGridAsync(configPath, CancellationToken.None);

        Assert.Equal(InstalledMetaGridState.MalformedFile, result.State);
        Assert.Null(result.InstalledHash);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task InspectInstalledMetaGridAsync_ReturnsNoManagedGrid_WhenOnlyCustomLayoutsExist()
    {
        using var temp = new TemporaryDirectory();
        var service = new DotaGridService();
        var configPath = Path.Combine(temp.Path, "hero_grid_config.json");
        var file = new DotaHeroGridFile
        {
            Configs =
            [
                new DotaHeroGridLayout
                {
                    ConfigName = "Custom Layout",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Favorites",
                            HeroIds = [1, 2, 3],
                            XPosition = 0,
                            YPosition = 0,
                            Width = 100,
                            Height = 100
                        }
                    ]
                }
            ]
        };
        await File.WriteAllTextAsync(configPath, service.Serialize(file));

        var result = await service.InspectInstalledMetaGridAsync(configPath, CancellationToken.None);

        Assert.Equal(InstalledMetaGridState.NoManagedGrid, result.State);
        Assert.Null(result.InstalledHash);
    }

    [Fact]
    public async Task InspectInstalledMetaGridAsync_ReturnsPresent_WhenManagedGridExists()
    {
        using var temp = new TemporaryDirectory();
        var service = new DotaGridService();
        var configPath = Path.Combine(temp.Path, "hero_grid_config.json");
        var snapshot = StubProvider.CreateSnapshot([1, 2, 3, 4, 5]);
        await File.WriteAllTextAsync(configPath, service.Serialize(service.ApplySnapshot(new DotaHeroGridFile(), snapshot)));

        var result = await service.InspectInstalledMetaGridAsync(configPath, CancellationToken.None);

        Assert.Equal(InstalledMetaGridState.Present, result.State);
        Assert.Equal(service.ComputeSnapshotHash(snapshot), result.InstalledHash);
        Assert.True(result.ManagedLayoutCount > 0);
    }

    [Fact]
    public void TryValidate_ReturnsFalse_ForMalformedJson()
    {
        var service = new DotaGridService();
        Assert.False(service.TryValidate("{ invalid json", out _));
    }

    [Fact]
    public void Serialize_PreservesUnknownTopLevelFields()
    {
        var service = new DotaGridService();
        var json = """
        {
          "version": 3,
          "configs": [],
          "custom_field": {
            "safe": true
          }
        }
        """;

        var file = JsonSerializer.Deserialize<DotaHeroGridFile>(json)!;
        var serialized = service.Serialize(file);

        Assert.Contains("custom_field", serialized);
    }

    [Fact]
    public async Task CanonicalHash_Matches_Serialize_Reread_ManagedGrid_Hash()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var service = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var configPath = Path.Combine(paths.RootDirectory, "steam", "111", "570", "remote", "cfg", "hero_grid_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var expectedHash = service.ComputeSnapshotHash(snapshot);
        var file = service.ApplySnapshot(new DotaHeroGridFile(), snapshot);
        await File.WriteAllTextAsync(configPath, service.Serialize(file));
        var reread = await service.ReadAsync(configPath, CancellationToken.None);
        var installedHash = service.ReadInstalledMetaGridHash(reread);

        Assert.Equal(expectedHash, installedHash);
    }

    [Fact]
    public void CanonicalHash_Treats_CategoryOrder_AsSemantic()
    {
        var service = new DotaGridService();
        var baseSnapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var reorderedCategories = baseSnapshot.Layouts[0].Categories.Skip(1).Concat(baseSnapshot.Layouts[0].Categories.Take(1)).ToList();
        var reorderedSnapshot = new HeroGridSnapshot
        {
            SourceName = baseSnapshot.SourceName,
            ProviderName = baseSnapshot.ProviderName,
            SourceStrategy = baseSnapshot.SourceStrategy,
            Preset = baseSnapshot.Preset,
            PatchLabel = baseSnapshot.PatchLabel,
            CapturedAt = baseSnapshot.CapturedAt,
            Hash = baseSnapshot.Hash,
            RankBracket = baseSnapshot.RankBracket,
            PeriodStart = baseSnapshot.PeriodStart,
            PeriodEnd = baseSnapshot.PeriodEnd,
            MinimumSampleMatches = baseSnapshot.MinimumSampleMatches,
            UsesDerivedPositions = baseSnapshot.UsesDerivedPositions,
            PositionDefinition = baseSnapshot.PositionDefinition,
            RoleResults = baseSnapshot.RoleResults,
            ProviderStatus = baseSnapshot.ProviderStatus,
            OriginKind = baseSnapshot.OriginKind,
            SourceDetails = baseSnapshot.SourceDetails,
            Layouts =
            [
                new HeroGridLayout
                {
                    Name = baseSnapshot.Layouts[0].Name,
                    Categories = reorderedCategories
                }
            ]
        };

        Assert.NotEqual(service.ComputeSnapshotHash(baseSnapshot), service.ComputeSnapshotHash(reorderedSnapshot));
    }

    [Fact]
    public void CanonicalHash_Ignores_ProviderMetadata_And_Is_CultureInvariant()
    {
        var service = new DotaGridService();
        var snapshot = StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "OpenDota");
        var metadataVariant = new HeroGridSnapshot
        {
            SourceName = "Different Source",
            ProviderName = "Different Provider",
            SourceStrategy = "Different Strategy",
            Preset = snapshot.Preset,
            PatchLabel = "Different Patch",
            CapturedAt = snapshot.CapturedAt.AddHours(5),
            Hash = snapshot.Hash,
            ProviderStatus = ProviderStatus.Cached,
            OriginKind = GridOriginKind.Cached,
            SourceDetails = "Different metadata",
            Layouts = snapshot.Layouts
        };

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            Assert.Equal(service.ComputeSnapshotHash(snapshot), service.ComputeSnapshotHash(metadataVariant));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task ApplySnapshot_PreservesOfficialD2ptCanonicalHash_AfterSerializeAndReread()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dota2protracker_hero_grid_high_winrate_config.json"));
        var validation = D2ptOfficialGridPayloadValidator.Validate(payload, "dota2protracker_hero_grid_high_winrate_config.json");
        Assert.True(validation.IsValid, validation.Message);

        var snapshot = new HeroGridSnapshot
        {
            SourceName = validation.Snapshot!.SourceName,
            ProviderName = validation.Snapshot.ProviderName,
            SourceStrategy = "OfficialDownloadPayload",
            Preset = validation.Snapshot.Preset,
            PatchLabel = validation.Snapshot.PatchLabel,
            CapturedAt = validation.Snapshot.CapturedAt,
            Hash = validation.SemanticHash!,
            Layouts = validation.Snapshot.Layouts,
            RankBracket = validation.Snapshot.RankBracket,
            PeriodStart = validation.Snapshot.PeriodStart,
            PeriodEnd = validation.Snapshot.PeriodEnd,
            MinimumSampleMatches = validation.Snapshot.MinimumSampleMatches,
            UsesDerivedPositions = validation.Snapshot.UsesDerivedPositions,
            PositionDefinition = validation.Snapshot.PositionDefinition,
            RoleResults = [],
            ProviderStatus = ProviderStatus.Online,
            OriginKind = GridOriginKind.NativeD2pt,
            SourceDetails = validation.Snapshot.SourceDetails,
            RawSha256 = validation.RawSha256,
            OriginalFilename = validation.Snapshot.OriginalFilename,
            ConfigCount = validation.Snapshot.ConfigCount,
            PayloadSizeBytes = validation.Snapshot.PayloadSizeBytes,
            ParsedGrid = validation.GridFile,
            RawPayloadBytes = System.Text.Encoding.UTF8.GetBytes(payload)
        };

        var service = new DotaGridService();
        var configPath = Path.Combine(paths.RootDirectory, "steam", "111", "570", "remote", "cfg", "hero_grid_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var file = service.ApplySnapshot(new DotaHeroGridFile(), snapshot);
        await File.WriteAllTextAsync(configPath, service.Serialize(file));
        var installedHash = await service.ReadInstalledMetaGridHashAsync(configPath, CancellationToken.None);

        Assert.Equal(validation.SemanticHash, installedHash);
    }

    [Fact]
    public async Task Parse_DotaNativeTestFixture_Succeeds_And_Test_Is_Stored_As_Configs_Array_Element()
    {
        var service = new DotaGridService();
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dota_native_test_grid.json");

        var json = await File.ReadAllTextAsync(path);
        Assert.True(service.TryValidate(json, out _));

        using var document = JsonDocument.Parse(json);
        var configs = document.RootElement.GetProperty("configs");
        Assert.Equal(JsonValueKind.Array, configs.ValueKind);
        var test = configs.EnumerateArray().Single(item => item.GetProperty("config_name").GetString() == "TEST");
        Assert.True(test.TryGetProperty("categories", out var categories));
        Assert.Equal(JsonValueKind.Array, categories.ValueKind);

        var model = JsonSerializer.Deserialize<DotaHeroGridFile>(json)!;
        var validation = service.ValidateNativeStructure(model);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Single(model.Configs);
        Assert.Equal("TEST", model.Configs[0].ConfigName);
    }

    [Fact]
    public async Task ApplySnapshot_Preserves_DotaNative_Test_Grid_Unchanged()
    {
        var service = new DotaGridService();
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dota_native_test_grid.json");
        var originalJson = await File.ReadAllTextAsync(path);
        var original = JsonSerializer.Deserialize<DotaHeroGridFile>(originalJson)!;
        var merged = service.ApplySnapshot(original, StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray()));

        var test = merged.Configs.Single(config => config.ConfigName == "TEST");
        var sourceTest = JsonSerializer.Deserialize<DotaHeroGridFile>(originalJson)!.Configs.Single(config => config.ConfigName == "TEST");

        Assert.Equal(sourceTest.Categories.Count, test.Categories.Count);
        Assert.Equal(
            sourceTest.Categories.Select(category => $"{category.CategoryName}:{category.XPosition}:{category.YPosition}:{category.Width}:{category.Height}:{string.Join(",", category.HeroIds)}"),
            test.Categories.Select(category => $"{category.CategoryName}:{category.XPosition}:{category.YPosition}:{category.Width}:{category.Height}:{string.Join(",", category.HeroIds)}"));
    }

    [Fact]
    public async Task ApplySnapshot_With_DotaNative_Test_Grid_Produces_Unique_Config_Names_And_Valid_Native_Structure()
    {
        var service = new DotaGridService();
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dota_native_test_grid.json");
        var original = JsonSerializer.Deserialize<DotaHeroGridFile>(await File.ReadAllTextAsync(path))!;
        var personalized = service.ApplySnapshot(original, StubProvider.CreateSnapshot(Enumerable.Range(1, 55).ToArray(), sourceName: "Dota2ProTracker"));

        var validation = service.ValidateNativeStructure(personalized);

        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(personalized.Configs.Count, personalized.Configs.Select(config => config.ConfigName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(personalized.Configs, config => config.ConfigName == "TEST");
        Assert.Equal(2, personalized.Configs.Count);
    }

    [Fact]
    public void ValidateNativeStructure_Fails_When_ConfigNames_Are_Duplicated()
    {
        var service = new DotaGridService();
        var file = new DotaHeroGridFile
        {
            Configs =
            [
                new DotaHeroGridLayout
                {
                    ConfigName = "TEST",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Strength",
                            XPosition = 0,
                            YPosition = 0,
                            Width = 100,
                            Height = 100,
                            HeroIds = [1]
                        }
                    ]
                },
                new DotaHeroGridLayout
                {
                    ConfigName = "TEST",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Agility",
                            XPosition = 0,
                            YPosition = 0,
                            Width = 100,
                            Height = 100,
                            HeroIds = [2]
                        }
                    ]
                }
            ]
        };

        var validation = service.ValidateNativeStructure(file);

        Assert.False(validation.IsValid);
        Assert.Contains("Duplicate config_name", validation.Error);
    }

    [Fact]
    public void ValidateNativeStructure_Fails_When_Category_Has_Invalid_Dimensions()
    {
        var service = new DotaGridService();
        var file = new DotaHeroGridFile
        {
            Configs =
            [
                new DotaHeroGridLayout
                {
                    ConfigName = "TEST",
                    Categories =
                    [
                        new DotaHeroGridCategory
                        {
                            CategoryName = "Strength",
                            XPosition = 0,
                            YPosition = 0,
                            Width = 0,
                            Height = 100,
                            HeroIds = [1]
                        }
                    ]
                }
            ]
        };

        var validation = service.ValidateNativeStructure(file);

        Assert.False(validation.IsValid);
        Assert.Contains("invalid width/height", validation.Error);
    }
}
