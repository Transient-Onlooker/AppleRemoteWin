// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854TouchDiagnosticsTests
{
    [Fact]
    public void Five_second_summary_reports_pipeline_counts_without_payloads()
    {
        var diagnostics = new A2854TouchDiagnostics();
        diagnostics.RawTouch();
        diagnostics.HidAccepted();
        diagnostics.InputGateDelivered();
        diagnostics.SessionDecoded();
        long queued = diagnostics.UiQueued();
        diagnostics.UiApplied(queued);
        diagnostics.UiHeartbeat();
        diagnostics.Suppressed(A2854TouchDiagnostics.NoMoveReason.PressureGuard);
        string summary = diagnostics.DrainSummary();

        Assert.Contains("raw=1 hid=1 gate=1 session=1", summary);
        Assert.Contains("uiQueued=1 uiDone=1 uiDrop=0", summary);
        Assert.Contains("noMove[contact,first,jump,wobble,drag,pressure,subpx]=0,0,0,0,0,1,0", summary);
        Assert.DoesNotContain("0xFC", summary);
        Assert.Contains("raw=0 hid=0 gate=0 session=0", diagnostics.DrainSummary());
    }

    [Fact]
    public void Dropped_ui_work_is_counted_and_reset_clears_all_stages()
    {
        var diagnostics = new A2854TouchDiagnostics();
        diagnostics.RawTouch();
        diagnostics.UiQueued();
        diagnostics.UiDropped();
        diagnostics.MoveResult(false);
        var first = diagnostics.DrainSummary();
        Assert.Contains("mouseFail=1", first);
        Assert.Contains("uiDrop=1", first);
        diagnostics.Reset();
        var second = diagnostics.DrainSummary();
        Assert.Contains("raw=0 hid=0", second);
        Assert.Contains("mouse=0 mouseFail=0", second);
        Assert.Contains("uiDrop=0", second);
    }

    [Fact]
    public void Pointer_mapper_exposes_no_move_reasons_without_changing_output()
    {
        var sink = new FakePointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        var reasons = new List<A2854TouchDiagnostics.NoMoveReason>();
        mapper.NoMoveObserved = reasons.Add;
        mapper.OnEvent(new TouchEvent(0, 0, 0, true, [new Finger(0, 0, 4)], []));
        mapper.OnEvent(new TouchEvent(0, 50, 0, true, [new Finger(50, 0, 4)], []));
        mapper.OnEvent(new TouchEvent(0, 500, 0, true, [new Finger(500, 0, 4)], []));

        Assert.Contains(A2854TouchDiagnostics.NoMoveReason.FirstContact, reasons);
        Assert.Contains(A2854TouchDiagnostics.NoMoveReason.Discontinuity, reasons);
        Assert.Equal((10, 0), Assert.Single(sink.Moves));
    }

    sealed class FakePointer : IRelativePointerSink
    {
        public readonly List<(int X, int Y)> Moves = [];
        public void Move(int dx, int dy) => Moves.Add((dx, dy));
        public bool LeftButton(bool down) => true;
    }
}
