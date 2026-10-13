using System;
using System.Collections.Generic;
using System.IO;
using LumiereMediaPlayer.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LumiereMediaPlayer.Tests.Helpers;

[TestClass]
public class AppIconHelperTests
{
    [TestMethod]
    public void GetCurrentVariantName_ReturnsValidFormat()
    {
        string variant = AppIconHelper.GetCurrentVariantName();
        Assert.IsNotNull(variant);
        StringAssert.StartsWith(variant, "Lumiere_");
        Assert.IsTrue(variant.Contains("Dark") || variant.Contains("Light"));
    }

    [TestMethod]
    [DataRow("Lumiere_Dark_Blue.ico")]
    [DataRow("Lumiere_Dark_Purple.ico")]
    [DataRow("Lumiere_Light_Teal.ico")]
    [DataRow("Lumiere_Light_Orange.ico")]
    public void VariantIcoFiles_ContainAllTargetShellResolutions(string icoFileName)
    {
        string? path = AppIconHelper.ResolveDiskPath(icoFileName);
        if (path == null)
        {
            path = Path.Combine(@"V:\Projects\Lumiere\Assets\Icons", icoFileName);
        }

        Assert.IsTrue(File.Exists(path), $"ICO file {icoFileName} should exist on disk");

        byte[] icoBytes = File.ReadAllBytes(path);
        Assert.IsTrue(icoBytes.Length >= 6, "ICO file must have valid header");

        ushort count = BitConverter.ToUInt16(icoBytes, 4);
        Assert.IsTrue(count >= 8, "ICO file must contain at least 8 multi-resolution frames");

        var foundSizes = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int entryOffset = 6 + (i * 16);
            int width = icoBytes[entryOffset];
            if (width == 0) width = 256;
            foundSizes.Add(width);

            uint imgOffset = BitConverter.ToUInt32(icoBytes, entryOffset + 12);
            // Verify PNG magic header 0x89 'P' 'N' 'G'
            Assert.AreEqual((byte)0x89, icoBytes[imgOffset]);
            Assert.AreEqual((byte)0x50, icoBytes[imgOffset + 1]);
            Assert.AreEqual((byte)0x4E, icoBytes[imgOffset + 2]);
            Assert.AreEqual((byte)0x47, icoBytes[imgOffset + 3]);
        }

        Assert.IsTrue(foundSizes.Contains(16), "Missing 16px frame");
        Assert.IsTrue(foundSizes.Contains(20), "Missing 20px frame");
        Assert.IsTrue(foundSizes.Contains(24), "Missing 24px frame");
        Assert.IsTrue(foundSizes.Contains(32), "Missing 32px frame");
        Assert.IsTrue(foundSizes.Contains(48), "Missing 48px frame");
        Assert.IsTrue(foundSizes.Contains(64), "Missing 64px frame");
        Assert.IsTrue(foundSizes.Contains(128), "Missing 128px frame");
        Assert.IsTrue(foundSizes.Contains(256), "Missing 256px frame");
    }

    [TestMethod]
    public void SyncShellAssetsAndShortcuts_ExecutesWithoutExceptions()
    {
        string variant = AppIconHelper.GetCurrentVariantName();
        AppIconHelper.SyncShellAssetsAndShortcuts(variant);
    }
}
