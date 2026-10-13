using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class BrightnessOverrideHelperTests
{
    [TestInitialize]
    public void Setup()
    {
        BrightnessOverrideHelper.ResetBaselineForTesting();
    }

    [TestCleanup]
    public void Cleanup()
    {
        BrightnessOverrideHelper.ResetBaselineForTesting();
    }

    [TestMethod]
    public void BrightnessOverrideHelper_InitialState_IsNotOverrideActive()
    {
        using var helper = new BrightnessOverrideHelper();
        Assert.IsFalse(helper.IsOverrideActive);
        Assert.IsFalse(BrightnessOverrideHelper.IsAnyOverrideActive);
    }

    [TestMethod]
    public void BrightnessOverrideHelper_Release_WhenInactive_DoesNotThrow()
    {
        using var helper = new BrightnessOverrideHelper();
        helper.Release();
        Assert.IsFalse(helper.IsOverrideActive);
        Assert.IsFalse(BrightnessOverrideHelper.IsAnyOverrideActive);
    }

    [TestMethod]
    public void BrightnessOverrideHelper_Dispose_MultipleTimes_DoesNotThrow()
    {
        var helper = new BrightnessOverrideHelper();
        helper.Dispose();
        helper.Dispose();
        Assert.IsFalse(helper.IsOverrideActive);
    }

    [TestMethod]
    public void BrightnessOverrideHelper_ResetBaseline_ClearsBaselineValues()
    {
        Assert.IsNull(BrightnessOverrideHelper.PersistentBaselineWmi);
        Assert.IsNull(BrightnessOverrideHelper.PersistentBaselineDdc);

        BrightnessOverrideHelper.ResetBaselineForTesting();

        Assert.IsNull(BrightnessOverrideHelper.PersistentBaselineWmi);
        Assert.IsNull(BrightnessOverrideHelper.PersistentBaselineDdc);
    }

    [TestMethod]
    public void IHdrPipelineService_SetVideoPageActive_CanBeCalledOnMock()
    {
        var mockHdr = new Mock<IHdrPipelineService>();
        mockHdr.Setup(h => h.SetVideoPageActive(It.IsAny<bool>())).Verifiable();

        mockHdr.Object.SetVideoPageActive(true);
        mockHdr.Object.SetVideoPageActive(false);

        mockHdr.Verify(h => h.SetVideoPageActive(true), Times.Once);
        mockHdr.Verify(h => h.SetVideoPageActive(false), Times.Once);
    }

    [TestMethod]
    public void IHdrPipelineService_SetSuspended_CanBeCalledOnMock()
    {
        var mockHdr = new Mock<IHdrPipelineService>();
        mockHdr.Setup(h => h.SetSuspended(It.IsAny<bool>())).Verifiable();

        mockHdr.Object.SetSuspended(true);
        mockHdr.Object.SetSuspended(false);

        mockHdr.Verify(h => h.SetSuspended(true), Times.Once);
        mockHdr.Verify(h => h.SetSuspended(false), Times.Once);
    }
}
