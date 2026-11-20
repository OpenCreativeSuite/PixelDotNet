using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class SessionAdjustmentTests
{
    [Fact]
    public void ToneSlidersRebuildFromTheSnapshotAndResetRestoresIt()
    {
        PaintSession session = TestHarness.Session(8, 8);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(128, 128, 128));
        uint original = layer.At(0, 0);

        session.AdjustBrightness = 50f;
        session.TouchAdjustments();
        Assert.True(Rgba.R(layer.At(0, 0)) > 128);
        Assert.True(session.HasAdjustments);

        session.AdjustContrast = 20f;
        session.TouchAdjustments();
        session.ResetAdjustments();

        Assert.Equal(original, layer.At(0, 0));
        Assert.False(session.HasAdjustments);
    }

    [Fact]
    public void LevelsAndCurveSlidersApplyOnTopOfTheSnapshot()
    {
        PaintSession session = TestHarness.Session(8, 8);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(128, 128, 128));

        session.LevelsBlack = 128f;
        session.TouchAdjustments();
        Assert.Equal(0, Rgba.R(layer.At(0, 0)));

        session.ResetLevels();
        Assert.Equal(128, Rgba.R(layer.At(0, 0)));

        session.LevelsBlack = 0f;
        session.LevelsWhite = 255f;
        session.LevelsGamma = 1f;
        int index = session.AddCurvePoint(0.5f, 0.25f);
        session.TouchAdjustments();

        Assert.Equal(1, index);
        Assert.True(session.HasCurve);
        Assert.True(Rgba.R(layer.At(0, 0)) < 128);

        session.ResetCurve();
        Assert.False(session.HasCurve);
    }

    [Fact]
    public void CurvePointsStayOrderedAndEndpointsStayFixed()
    {
        PaintSession session = TestHarness.Session();

        int index = session.AddCurvePoint(0.65f, 0.4f);
        Assert.Equal(1, index);
        Assert.Equal(3, session.CurvePoints.Count);

        session.MoveCurvePoint(index, 0.99f, 1.5f);
        Assert.InRange(session.CurvePoints[index].X, 0.7f, 0.998f);
        Assert.Equal(1f, session.CurvePoints[index].Y);

        Assert.Equal(index, session.FindCurvePoint(session.CurvePoints[index].X, session.CurvePoints[index].Y, 0.1f));
        Assert.Equal(-1, session.FindCurvePoint(0.2f, 0.9f, 0.01f));

        session.RemoveCurvePoint(0);
        session.RemoveCurvePoint(index);
        Assert.Equal(2, session.CurvePoints.Count);
    }

    [Fact]
    public void AdjustmentLayersEditTheirOwnTargets()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.AddAdjustmentLayer(AdjustmentKind.Levels);

        PaintLayer? adjustment = session.ActiveAdjustmentLayer;
        Assert.NotNull(adjustment);
        Assert.True(adjustment!.IsAdjustment);
        Assert.Equal(AdjustmentKind.Levels, adjustment.Adjustment);

        session.SetAdjustmentLevels(10f, 200f, 2f);
        Assert.Equal(10f, adjustment.AdjustmentBlack);
        Assert.Equal(200f, adjustment.AdjustmentWhite);
        Assert.Equal(2f, adjustment.AdjustmentGamma);

        session.SetAdjustmentLevels(-10f, 999f, 99f);
        Assert.Equal(0f, adjustment.AdjustmentBlack);
        Assert.Equal(255f, adjustment.AdjustmentWhite);
        Assert.Equal(5f, adjustment.AdjustmentGamma);

        session.ResetAdjustmentLevels();
        Assert.Equal(0f, adjustment.AdjustmentBlack);
        Assert.Equal(255f, adjustment.AdjustmentWhite);
        Assert.Equal(1f, adjustment.AdjustmentGamma);

        session.AddAdjustmentLayer(AdjustmentKind.Curves);
        int point = session.AddActiveCurvePoint(0.5f, 0.75f);
        Assert.Equal(1, point);
        session.MoveActiveCurvePoint(point, 0.6f, 0.8f);
        Assert.Equal(point, session.FindActiveCurvePoint(0.6f, 0.8f, 0.01f));
        session.ResetActiveCurve();
        Assert.Equal(2, session.ActiveCurvePoints.Count);
    }

    [Fact]
    public void AddingAnAdjustmentLayerAboveARasterLayerChangesTheComposite()
    {
        PaintSession session = TestHarness.Session(4, 4);
        session.Document.Active.Fill(Rgba.FromRgb(128, 128, 128));
        session.AddAdjustmentLayer(AdjustmentKind.Levels);
        session.SetAdjustmentLevels(128f, 255f, 1f);

        uint pixel = session.Document.CompositeAt(0, 0);

        Assert.Equal(0, Rgba.R(pixel));
        Assert.Equal(255, Rgba.A(pixel));
    }

    [Fact]
    public void AdjustmentsDoNotRunOnLockedLayers()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(128, 128, 128));
        layer.Locked = true;
        session.AdjustBrightness = 80f;

        session.TouchAdjustments();

        Assert.Equal(0xFF808080u, layer.At(0, 0));
        Assert.Equal("Layer is locked", session.Status);
    }
}

public sealed class SessionEffectTests
{
    [Fact]
    public void EffectPreviewAppliesResetsAndRepeats()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        TestHarness.FillPattern(layer);
        uint original = layer.At(4, 4);

        session.SelectEffect(EffectKind.Pixelate);
        Assert.Equal(EffectKind.Pixelate, session.Effect);
        Assert.Equal(10f, session.EffectAmount);
        Assert.True(session.HasEffectPreview);

        session.SetEffectAmount(4f);
        Assert.Equal(4f, session.EffectAmount);

        session.ApplyEffect();
        Assert.Equal(EffectKind.Pixelate, session.LastEffect);
        Assert.False(session.HasEffectPreview);

        session.RepeatLastEffect();
        Assert.Contains("Pixelate", session.Status);

        session.SelectEffect(EffectKind.GaussianBlur);
        session.ResetEffect();
        Assert.Equal(EffectKind.None, session.Effect);
        Assert.False(session.HasEffectPreview);
    }

    [Fact]
    public void SwitchingEffectsPreviewsFromTheOriginalSnapshot()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        TestHarness.FillPattern(layer);
        uint[] original = layer.CapturePixels();

        session.SelectEffect(EffectKind.Posterize);
        session.SelectEffect(EffectKind.Vignette);
        session.ResetEffect();

        Assert.Equal(original, layer.Pixels);
    }

    [Fact]
    public void LockedLayersRejectEffects()
    {
        PaintSession session = TestHarness.Session();
        session.Document.Active.Locked = true;

        session.SelectEffect(EffectKind.Vignette);

        Assert.Equal(EffectKind.None, session.Effect);
        Assert.False(session.HasEffectPreview);
        Assert.Equal("Layer is locked", session.Status);
    }

    [Fact]
    public void InvertDesaturateAndFlipEditTheActiveLayer()
    {
        PaintSession session = TestHarness.Session(4, 4);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(255, 0, 0));

        session.Invert();
        Assert.Equal(0xFF00FFFFu, layer.At(0, 0));

        session.Desaturate();
        uint grey = layer.At(0, 0);
        Assert.Equal(Rgba.R(grey), Rgba.G(grey));
        Assert.Equal(Rgba.G(grey), Rgba.B(grey));

        layer.Fill(Rgba.FromRgb(1, 2, 3));
        layer.Put(0, 0, Rgba.FromRgb(255, 0, 0));
        session.FlipHorizontal();
        Assert.Equal(0xFFFF0000u, layer.At(3, 0));

        layer.Put(0, 0, Rgba.FromRgb(0, 255, 0));
        session.FlipVertical();
        Assert.Equal(0xFF00FF00u, layer.At(0, 3));
    }
}

public sealed class SessionShapeAndTextTests
{
    [Fact]
    public void DraggedShapeCreatesAnEditableShapeLayer()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShapeAsLayer = true;
        session.Tool = PaintTool.Rectangle;
        session.FillShapes = true;
        session.SetForeground(200, 10, 10);
        int before = session.Document.Layers.Count;

        session.PointerDown(2f, 2f);
        session.PointerMove(12f, 10f);
        session.PointerUp(12f, 10f);

        Assert.Equal(before + 1, session.Document.Layers.Count);
        PaintLayer shapeLayer = session.Document.Active;
        Assert.True(shapeLayer.IsShapeLayer);
        Assert.Equal(ShapeKind.Rectangle, shapeLayer.Shape!.Kind);
        Assert.Equal(2f, shapeLayer.Shape.X);
        Assert.Equal(10f, shapeLayer.Shape.Width);
        Assert.Equal(8f, shapeLayer.Shape.Height);
        Assert.NotEqual(0u, shapeLayer.At(6, 6));
    }

    [Fact]
    public void ShapeEditDialogKeepsOrRestoresTheGeometry()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShapeAsLayer = true;
        session.Tool = PaintTool.Ellipse;
        session.PointerDown(2f, 2f);
        session.PointerMove(12f, 10f);
        session.PointerUp(12f, 10f);
        PaintLayer layer = session.Document.Active;

        session.ShowShapeEditDialog();
        Assert.True(session.IsShapeEditOpen);
        Assert.Same(layer.Shape, session.ShapeEditTarget);

        session.ShapeEditTarget!.Width = 5f;
        session.TouchShape();
        session.CancelShapeEdit();
        Assert.Equal(10f, layer.Shape!.Width);

        session.ShowShapeEditDialog();
        session.ShapeEditTarget!.Width = 5f;
        session.TouchShape();
        session.CommitShapeEdit();
        Assert.False(session.IsShapeEditOpen);
        Assert.Equal(5f, layer.Shape!.Width);

        session.RasterizeShape();
        Assert.False(layer.IsShapeLayer);
    }

    [Fact]
    public void PenToolBuildsAPathAndPathNodesCanBeEdited()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.Tool = PaintTool.Pen;
        session.FillShapes = true;
        int before = session.Document.Layers.Count;

        session.PointerDown(2f, 2f);
        session.PointerDown(20f, 2f);
        session.PointerDown(20f, 18f);
        Assert.True(session.IsPenActive);
        Assert.Equal(3, session.PenPoints.Count);

        session.FinishPenPath();

        Assert.False(session.IsPenActive);
        Assert.Equal(before + 1, session.Document.Layers.Count);
        PaintLayer path = session.Document.Active;
        Assert.True(path.IsShapeLayer);
        Assert.Equal(ShapeKind.Path, path.Shape!.Kind);
        Assert.Equal(3, path.Shape.Points!.Count);
        Assert.True(session.CanEditPathNodes);

        session.StartPathNodeEdit();
        Assert.True(session.IsPathNodeEditing);
        session.PointerDown(2f, 2f);
        Assert.Equal(0, session.SelectedPathNode);

        session.AddPathNode();
        Assert.Equal(4, path.Shape.Points.Count);
        session.DeleteSelectedPathNode();
        Assert.Equal(3, path.Shape.Points.Count);

        session.EndPathNodeEdit();
        Assert.False(session.IsPathNodeEditing);
    }

    [Fact]
    public void PenPathCanBeCancelled()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Pen;
        session.PointerDown(3f, 3f);
        session.PointerDown(9f, 3f);

        session.CancelPenPath();

        Assert.False(session.IsPenActive);
        Assert.Equal("Pen cancelled", session.Status);
    }

    [Fact]
    public void TextToolCreatesBakesAndCancelsText()
    {
        PaintSession session = TestHarness.Session(64, 48);
        session.Tool = PaintTool.Text;
        session.TextAsLayer = true;
        int before = session.Document.Layers.Count;

        session.BeginText(4, 30);
        session.SetText("Hello");
        session.SetTextSize(24f);
        session.ToggleTextBold();
        session.ToggleTextItalic();
        session.SetTextAlign(TextAlign.Center);
        session.ApplyText();

        Assert.Equal(before + 1, session.Document.Layers.Count);
        PaintLayer textLayer = session.Document.Active;
        Assert.True(textLayer.IsTextLayer);
        Assert.Equal("Hello", textLayer.Text!.Content);
        Assert.True(textLayer.Text.Bold);
        Assert.True(textLayer.Text.Italic);
        Assert.Equal(TextAlign.Center, textLayer.Text.Align);
        Assert.Equal(string.Empty, session.TextContent);

        session.ShowTextEditDialog();
        Assert.True(session.IsTextEditOpen);
        session.TextEditTarget!.Content = "Changed";
        session.TouchTextLayer();
        session.CancelTextEdit();
        Assert.Equal("Hello", textLayer.Text!.Content);

        session.ShowTextEditDialog();
        session.TextEditTarget!.Content = "Changed";
        session.TouchTextLayer();
        session.CommitTextEdit();
        Assert.False(session.IsTextEditOpen);
        Assert.Equal("Changed", textLayer.Text!.Content);

        session.RasterizeText();
        Assert.False(textLayer.IsTextLayer);
    }

    [Fact]
    public void TextPreviewCanBePlacedOnTheActiveLayerOrCancelled()
    {
        PaintSession session = TestHarness.Session(48, 32);
        PaintLayer layer = session.Document.Active;
        session.Tool = PaintTool.Text;
        session.TextAsLayer = false;
        session.BeginText(4, 20);
        session.SetText("X");
        session.ApplyText();

        Assert.False(session.Document.Active.IsTextLayer);
        Assert.Contains("Text placed", session.Status);

        session.BeginText(4, 20);
        session.SetText("Y");
        session.CancelText();
        Assert.Equal("Text cancelled", session.Status);
        Assert.Equal(string.Empty, session.TextContent);
    }

    [Fact]
    public void TextFontMenuSearchesAndAppliesFamilies()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Text;

        session.ToggleFontMenu();
        Assert.True(session.IsFontMenuOpen);
        Assert.Equal(TextFonts.All.Length, session.FontMatchCount);

        session.SetFontSearch("Segoe");
        Assert.True(session.FontMatchCount >= 1);
        Assert.Contains("Segoe", session.Font.Name);

        session.BackspaceFontSearch();
        session.AppendFontSearch("e");
        session.FontMenuKey("Escape");
        Assert.False(session.IsFontMenuOpen);

        session.ToggleFontMenu();
        session.ApplyFont(1);
        Assert.False(session.IsFontMenuOpen);
        Assert.Equal(1, session.TextFontIndex);
        Assert.Equal(TextFonts.At(1).Name, session.Status);
    }

    [Fact]
    public void FontAddRowValidatesAndReportsImportResults()
    {
        PaintSession session = TestHarness.Session();

        session.OpenFontAdd();
        Assert.True(session.IsFontAddOpen);
        Assert.False(session.BeginFontImport());
        Assert.True(session.FontImportFailed);

        session.SetFontUrl("https://example.test/font");
        Assert.True(session.BeginFontImport());
        Assert.True(session.IsFontDownloading);

        session.FailFontImport("network down");
        Assert.False(session.IsFontDownloading);
        Assert.True(session.FontImportFailed);
        Assert.Contains("network", session.FontImportMessage);

        session.CancelFontAdd();
        Assert.False(session.IsFontAddOpen);
    }

    [Fact]
    public void TextEffectsPresetsAndColourTargetsUpdateThePreview()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Text;
        session.BeginText(4, 20);
        session.SetText("FX");

        session.ApplyTextPreset(TextPreset.Glow);
        Assert.Equal(TextPreset.Glow, session.TextPreset);
        Assert.True(session.TextShadow);
        Assert.Equal(0, session.TextShadowX);

        session.ApplyTextPreset(TextPreset.Neon);
        Assert.True(session.TextOutline);
        Assert.True(session.TextShadow);

        session.SetTextOutlineWidth(2f);
        Assert.Equal(TextPreset.Custom, session.TextPreset);
        session.SetTextShadowBlur(12f);
        session.SetTextShadowOffset(3, 4);
        session.SetTextShadowOpacity(80);
        Assert.Equal(80, session.TextShadowOpacity);

        session.ToggleTextFx();
        Assert.True(session.IsTextFxOpen);
        session.CloseTextFx();
        Assert.False(session.IsTextFxOpen);

        session.SetTextOutline(false);
        session.SetTextShadow(false);
        session.ApplyTextPreset(TextPreset.Outline);
        Assert.True(session.TextOutline);
        Assert.False(session.TextShadow);
    }

    [Fact]
    public void TextOnPathReusesPathGeometry()
    {
        PaintSession session = TestHarness.Session(64, 48);
        session.Tool = PaintTool.Pen;
        session.PointerDown(2f, 30f);
        session.PointerDown(30f, 10f);
        session.PointerDown(60f, 30f);
        session.FinishPenPath();
        PaintLayer path = session.Document.Active;

        session.CreateTextOnPath();

        PaintLayer text = session.Document.Active;
        Assert.True(text.IsTextLayer);
        Assert.Equal(path.Name, text.Text!.PathLayer);
        Assert.NotNull(text.Text.Path);
        Assert.True(session.IsTextEditOpen);
        session.CommitTextEdit();
    }
}

public sealed class SessionSmartAndStyleTests
{
    [Fact]
    public void SmartObjectsTransformAndRasterize()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(10, 20, 30));

        session.ConvertToSmartObject();
        Assert.True(layer.IsSmartObject);
        Assert.Equal(16, layer.SmartSourceWidth);

        session.ShowSmartTransformDialog();
        Assert.True(session.IsSmartTransformOpen);
        session.SetSmartValue(0, 3f);
        session.SetSmartValue(2, 150f);
        session.SetSmartValue(4, 15f);
        Assert.Equal(3f, session.SmartValue(0));
        Assert.Equal(150f, session.SmartValue(2));
        Assert.Equal(15f, session.SmartValue(4));

        session.ToggleSmartFlipX();
        session.ToggleSmartFlipY();
        Assert.True(session.SmartFlipX);
        Assert.True(session.SmartFlipY);

        session.CancelSmartTransform();
        Assert.False(session.IsSmartTransformOpen);
        Assert.Equal(0f, layer.SmartX);

        session.ShowSmartTransformDialog();
        session.SetSmartValue(1, 2f);
        session.CommitSmartTransform();
        Assert.Equal(2f, layer.SmartY);

        session.ShowSmartTransformDialog();
        session.ResetSmartTransform();
        Assert.Equal(0f, layer.SmartY);

        session.RasterizeSmartObject();
        Assert.False(layer.IsSmartObject);
    }

    [Fact]
    public void LayerStyleDialogTargetsTheRequestedLayer()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;

        session.ShowLayerStyleDialog(session.Document.ActiveIndex);
        Assert.True(session.IsLayerStyleOpen);
        Assert.Same(layer.Style, session.LayerStyleTarget);

        session.LayerStyleTarget!.DropShadow = true;
        session.LayerStyleTarget.Stroke = true;
        session.TouchLayerStyle();
        Assert.True(layer.Style.HasAny);

        session.CloseLayerStyleDialog();
        Assert.False(session.IsLayerStyleOpen);

        session.ShowLayerStyleDialog(-1);
        Assert.False(session.IsLayerStyleOpen);
    }

    [Fact]
    public void ColourToolsDialogAppliesEveryOperation()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.Document.Active.Fill(Rgba.FromRgb(128, 128, 128));
        session.SelectRect(0, 0, 4, 4);

        session.ShowColourToolsDialog();
        Assert.True(session.IsColourToolsOpen);
        Assert.Equal(128, session.ThresholdLevel);
        Assert.Equal(100f, session.ChannelMixValue(0));

        session.SetThresholdLevel("200");
        session.ApplyThreshold();
        Assert.Equal(0, Rgba.R(session.Document.Active.At(0, 0)));

        session.Document.Active.Fill(Rgba.FromRgb(128, 128, 128));
        session.SetThresholdLevel("abc");
        Assert.Equal(200, session.ThresholdLevel);

        session.SetGradientLow(Rgba.FromRgb(255, 0, 0));
        session.SetGradientHigh(Rgba.FromRgb(0, 0, 255));
        session.ApplyGradientMap();
        session.SetReplaceFrom(Rgba.FromRgb(10, 20, 30));
        session.SetReplaceTo(Rgba.FromRgb(40, 50, 60));
        session.SetReplaceTolerance("12");
        session.ApplyColourReplace();
        session.SetBalanceValue(0, 100f);
        session.SetBalanceValue(99, 100f);
        session.ApplyColourBalance();
        session.SetChannelMixValue(0, 0f);
        session.SetChannelMixValue(99, 100f);
        session.ApplyChannelMix();
        session.ResetChannelMix();
        Assert.Equal(100f, session.ChannelMixValue(0));

        session.SelectSelectiveRange(4);
        session.SetSelectiveValue(0, 20f);
        Assert.Equal(20f, session.SelectiveValue(0));
        session.ApplySelectiveColour();
        session.ResetSelectiveColour();
        Assert.Equal(0f, session.SelectiveValue(0));

        session.SetRedEyeStrength("50");
        Assert.Equal(50, session.RedEyeStrength);
        session.ApplyRedEye();

        int layers = session.Document.Layers.Count;
        session.ApplySplitChannels();
        Assert.Equal(layers + 3, session.Document.Layers.Count);

        session.CloseColourToolsDialog();
        Assert.False(session.IsColourToolsOpen);
    }

    [Fact]
    public void ContentAwareFillRequiresASelectionAndRestoresNearbyPixels()
    {
        PaintSession session = TestHarness.Session(8, 8);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(20, 40, 60));
        layer.Put(4, 4, Rgba.FromRgb(255, 0, 0));

        session.ContentAwareFill();
        Assert.Contains("selection", session.Status);

        session.SelectRect(3, 3, 3, 3);
        session.ContentAwareFill();

        Assert.Equal("Content-aware fill", session.Status);
        Assert.NotEqual(0xFFFF0000u, layer.At(4, 4));
    }

    [Fact]
    public void MeshWarpPreviewCanBeResetCommittedOrCancelled()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        TestHarness.FillPattern(layer);
        uint[] original = layer.CapturePixels();

        session.ShowMeshWarpDialog();
        Assert.True(session.IsMeshWarpOpen);
        Assert.False(session.MeshWarpLayerEmpty);

        session.ShowMeshWarpDialog();
        Assert.True(session.BeginMeshWarpDrag(8f, 8f, 1f));
        session.UpdateMeshWarpDrag(10f, 10f);
        Assert.True(session.MeshWarpActiveNode >= 0);
        session.EndMeshWarpDrag();
        Assert.True(TestHarness.Differs(original, layer.Pixels));

        session.ResetMeshWarp();
        Assert.Equal(original, layer.Pixels);

        session.SetMeshWarpOffset();
        session.CancelMeshWarp();
        Assert.Equal(original, layer.Pixels);
        Assert.False(session.IsMeshWarpOpen);

        session.ShowMeshWarpDialog();
        session.SetMeshWarpOffset();
        session.CommitMeshWarp();
        Assert.False(session.IsMeshWarpOpen);
        Assert.True(TestHarness.Differs(original, layer.Pixels));
    }

    [Fact]
    public void PerspectivePreviewCanBeResetCommittedOrCancelled()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        TestHarness.FillPattern(layer);
        uint[] original = layer.CapturePixels();

        session.ShowPerspectiveDialog();
        Assert.True(session.IsPerspectiveOpen);
        session.SetPerspectiveValue(0, 3f);
        Assert.Equal(3f, session.PerspectiveValue(0));
        session.ResetPerspective();
        Assert.Equal(0f, session.PerspectiveValue(0));
        session.CancelPerspective();
        Assert.Equal(original, layer.Pixels);

        session.ShowPerspectiveDialog();
        session.SetPerspectiveValue(2, 4f);
        session.CommitPerspective();
        Assert.False(session.IsPerspectiveOpen);
        Assert.True(TestHarness.Differs(original, layer.Pixels));
    }

    [Fact]
    public void LockedAndAdjustmentLayersRejectTransforms()
    {
        PaintSession session = TestHarness.Session();
        session.Document.Active.Locked = true;
        session.ShowPerspectiveDialog();
        Assert.False(session.IsPerspectiveOpen);
        session.ShowMeshWarpDialog();
        Assert.False(session.IsMeshWarpOpen);

        session.Document.Active.Locked = false;
        session.AddAdjustmentLayer(AdjustmentKind.Levels);
        session.ShowPerspectiveDialog();
        session.ShowMeshWarpDialog();
        Assert.Contains("Adjustment", session.Status);
    }
}

internal static class SessionTestExtensions
{
    /// <summary>Moves the centre mesh node so the preview contains a real warp.</summary>
    public static void SetMeshWarpOffset(this PaintSession session)
    {
        (float x, float y) = session.MeshNodePosition(4);
        session.BeginMeshWarpDrag(x, y, 1f);
        session.UpdateMeshWarpDrag(x + 3f, y + 3f);
        session.EndMeshWarpDrag();
    }
}
