using System;
using System.Collections.Generic;
using System.IO;
using KcpServer;
using KcpServer.Navigation;
using Xunit;

namespace KcpServer.Tests.Navigation;

public class NavDataFormatTests
{
    private static NavData Sample()
    {
        return new NavData
        {
            MapId = "test",
            SourceHash = 42UL,
            MinX = 0f, MinZ = 0f, MaxX = 2f, MaxZ = 2f,
            TileSize = 64f,
            AreaCosts = new[] { 1f, 1f, 2f },
            Vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(2f, 0f, 2f),
                new Vector3(0f, 0f, 2f)
            },
            Tiles = new[]
            {
                new NavTileData
                {
                    TileX = 0, TileZ = 0,
                    Indices = new[] { 0, 1, 2, 0, 2, 3 },
                    Areas = new byte[] { 0, 0 }
                }
            }
        };
    }

    [Fact]
    public void Write_Read_RoundTrip_Preserves_Geometry_And_Hash()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".navbin");
        try
        {
            NavData data = Sample();
            NavDataFormat.Write(path, data);
            Assert.True(File.Exists(path));

            NavData read = NavDataFormat.Read(path);
            Assert.Equal("test", read.MapId);
            Assert.Equal(42UL, read.SourceHash);
            Assert.Equal(4, read.Vertices.Length);
            Assert.Single(read.Tiles);
            Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, read.Tiles[0].Indices);
            Assert.Equal(data.CanonicalHash, read.CanonicalHash);
            Assert.Equal(3, read.AreaCosts.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Read_Truncated_File_Throws()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".navbin");
        try
        {
            NavDataFormat.Write(path, Sample());
            byte[] all = File.ReadAllBytes(path);
            File.WriteAllBytes(path, all[..(all.Length / 2)]);
            Assert.ThrowsAny<Exception>(() => NavDataFormat.Read(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CanonicalHash_Changes_When_Geometry_Changes()
    {
        NavData a = Sample();
        NavData b = Sample();
        b.Vertices = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2.1f, 0f, 0f),
            new Vector3(2f, 0f, 2f),
            new Vector3(0f, 0f, 2f)
        };

        Assert.NotEqual(
            NavDataFormat.ComputeCanonicalHash(a.Vertices, a.Tiles),
            NavDataFormat.ComputeCanonicalHash(b.Vertices, b.Tiles));
    }
}
