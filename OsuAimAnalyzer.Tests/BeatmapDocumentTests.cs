using System.Globalization;
using System.Text;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class BeatmapDocumentTests
{
    internal const string Map = """
        osu file format v14
        // retained comment
        [General]
        AudioFilename: audio/song.mp3
        AudioLeadIn: 100
        PreviewTime: -1
        Mode: 0
        [Editor]
        Bookmarks: 101,200
        [Metadata]
        Title: Original title
        BeatmapID: 123
        [Difficulty]
        HPDrainRate:5
        CircleSize:4
        OverallDifficulty:6
        ApproachRate:7
        SliderMultiplier:1.4
        [Events]
        0,0,"images/bg,wide.jpg",0,0
        2,1000,2000
        Sample,1200,0,"sounds/click.wav",80
        [TimingPoints]
        -100,500,4,2,1,80,1,0
        500,-50,4,2,1,80,0,0
        1000,400,4,2,1,80,1,0
        [HitObjects]
        100,100,101,1,0,0:0:0:0:sounds/hit.wav
        200,100,500,2,0,B|220:120|220:120|240:100,2,80,0|0|0,0:0|0:0|0:0,0:0:0:0:
        300,100,900,1,0,0:0:0:0:
        256,192,2200,8,0,2500,0:0:0:0:
        """;
    internal static PracticeTransformOptions Options(double rate = 1, double spacing = 1, SerializedDifficulty? difficulty = null)
        => new(rate, spacing, new(5, 4, 7, 6), difficulty ?? new(5, 4, 7, 6), PracticePitchPolicy.PreservePitch);

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    [InlineData("\r", true)]
    public void Document_RoundTripPreservesBytesAndIdentity(string ending, bool bom)
    {
        var encoding = new UTF8Encoding(bom);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(Map.ReplaceLineEndings(ending) + ending + "[Unknown]" + ending + "retained")).ToArray();
        var document = BeatmapDocument.FromBytes(bytes);
        Assert.Equal(bytes, document.ToBytes());
        Assert.Equal(bytes, BeatmapTransforms.Apply(document, Options()).Document.ToBytes());
    }

    [Fact]
    public void Document_Utf16AndMixedEndingsRemainIntact()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("日本語\r\n// x\nlast\r")).ToArray();
        Assert.Equal(bytes, BeatmapDocument.FromBytes(bytes).ToBytes());
        Assert.Throws<DecoderFallbackException>(() => BeatmapDocument.FromBytes(new byte[] { 0xff }));
    }

    [Fact]
    public void Rate_RetimesAllSupportedClocksWithoutChangingInheritedVelocityOrSource()
    {
        var source = BeatmapDocument.Parse(Map);
        var result = BeatmapTransforms.Apply(source, Options(.5));
        string text = result.Document.ToString();
        Assert.Contains("-200,1000,4", text);
        Assert.Contains("1000,-50,4", text);
        Assert.Contains("2000,800,4", text);
        Assert.Contains("Bookmarks:202,400", text);
        Assert.Contains("PreviewTime: -1", text);
        Assert.Contains("AudioLeadIn:200", text);
        Assert.Contains("2,2000,4000", text);
        Assert.Contains("Sample,2400,0", text);
        Assert.Contains("256,192,4400,8,0,5000", text);
        Assert.Contains("B|220:120|220:120|240:100,2,80", text);
        Assert.Equal(Map, source.ToString());
        Assert.True(result.RequiresAudioRendering);
        Assert.Empty(BeatmapParser.ParseDocument(result.Document).StandardStars);
        Assert.Equal("", BeatmapParser.ParseDocument(result.Document).Hash);
        Assert.Equal("Original title", BeatmapParser.ParseDocument(result.Document).Title);
    }

    [Fact]
    public void CombinedTransform_PreservesSliderShapeRepeatsAndLengthAndUsesExplicitStats()
    {
        var result = BeatmapTransforms.Apply(BeatmapDocument.Parse(Map), Options(.5, .5, new(3, 3, 6, 5)));
        string text = result.Document.ToString();
        Assert.Contains("150,100,1000,2,0,B|170:120|170:120|190:100,2,80", text);
        Assert.Contains("200,100,1800,1", text); // even repeat exits at its head
        Assert.Contains("ApproachRate:6", text);
        Assert.Equal(.5, result.AchievedHeadSpacingRatio, 8);
        Assert.Equal(6, BeatmapParser.ParseDocument(result.Document).AR);
    }

    [Fact]
    public void Spacing_OddRepeatUsesTailProxyAndReportsAchievedHeadRatio()
    {
        var document = BeatmapDocument.Parse(Map.Replace(",2,80,0|0|0", ",1,80,0|0|0"));
        var result = BeatmapTransforms.Apply(document, Options(spacing: .5));
        Assert.Contains("220,100,900,1", result.Document.ToString());
        Assert.Equal(.6, result.AchievedHeadSpacingRatio, 8);
        Assert.Contains("proxy", result.SpacingMeasurement);
    }

    [Theory]
    [InlineData("Sprite,Background,Centre,\"sprite.png\",0,0")]
    [InlineData(" F,0,100,200,0,1")]
    [InlineData("Video,0,\"video.mp4\"")]
    [InlineData("2,100")]
    [InlineData("2,500,100")]
    public void Rate_UnsupportedOrMalformedEventsFailExplicitly(string entry)
    {
        var document = BeatmapDocument.Parse(Map.Replace("2,1000,2000", entry));
        Assert.Throws<NotSupportedException>(() => BeatmapTransforms.Apply(document, Options(.8)));
        Assert.Equal(document.ToString(), BeatmapTransforms.Apply(document, Options()).Document.ToString());
    }

    [Theory]
    [InlineData("Mode: 0", "Mode: 3")]
    [InlineData("500,-50", "500,NaN")]
    [InlineData("500,-50", "500,50")]
    [InlineData("220:120", "bad")]
    [InlineData("100,100,101", "100,100,101.5")]
    public void Transform_InvalidInputCannotProducePartialDocument(string oldText, string newText)
    {
        var doc = BeatmapDocument.Parse(Map.Replace(oldText, newText));
        Assert.Throws<NotSupportedException>(() => BeatmapTransforms.Apply(doc, Options(.8)));
    }

    [Fact]
    public void Spacing_RejectsOutOfBoundsInsteadOfClampingSliderShape()
    {
        var doc = BeatmapDocument.Parse(Map.Replace("220:120", "900:120"));
        Assert.Throws<NotSupportedException>(() => BeatmapTransforms.Apply(doc, Options(spacing: .5)));
    }

    [Fact]
    public void Transform_IsCultureIndependentAndRoundsHalfAwayFromZero()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = BeatmapTransforms.Apply(BeatmapDocument.Parse(Map), Options(2));
            Assert.Contains("100,100,51,1", result.Document.ToString());
            Assert.Contains("-50,250,4", result.Document.ToString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Resources_CollectsNestedAndQuotedPathsAndFlagsTraversal()
    {
        var manifest = BeatmapResources.Inspect(BeatmapDocument.Parse(Map));
        Assert.Contains(manifest.Files, f => f.RelativePath == "images/bg,wide.jpg" && f.Kind == "background");
        Assert.Contains(manifest.Files, f => f.RelativePath == "sounds/hit.wav");
        Assert.Contains(manifest.Files, f => f.RelativePath == "audio/song.mp3");
        var unsafeManifest = BeatmapResources.Inspect(BeatmapDocument.Parse(Map.Replace("audio/song.mp3", "../song.mp3")));
        Assert.DoesNotContain(unsafeManifest.Files, f => f.Kind == "audio");
        Assert.Contains(unsafeManifest.Issues, i => i.StartsWith("Unsafe"));
        Assert.Contains(manifest.Issues, i => i.Contains("implicit"));
    }

    [Fact]
    public void Preview_UsesExistingParserWithoutSourceCacheOrFileMutation()
    {
        string path = Path.Combine(Path.GetTempPath(), "analyzer-" + Guid.NewGuid() + ".osu");
        try
        {
            File.WriteAllText(path, Map);
            byte[] original = File.ReadAllBytes(path);
            var cached = new OsuDbBeatmap { Md5 = new string('a', 32), StandardStars = new Dictionary<int, double> { [0] = 6.5 } };
            var existing = BeatmapParser.Parse(path, cached);
            var result = BeatmapTransforms.Apply(BeatmapDocument.FromBytes(original), Options(.75));
            var preview = BeatmapParser.ParseDocument(result.Document);
            Assert.Equal(6.5, existing.StandardStars[0]);
            Assert.Empty(preview.StandardStars);
            Assert.Equal(existing.HitObjects.Count, preview.HitObjects.Count);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("[Unknown]\nData: retained")]
    [InlineData("[Difficulty]\nCircleSize:4")]
    public void Rate_UnknownSectionsAndDuplicateStatsAreRejected(string extra)
        => Assert.Throws<NotSupportedException>(() => BeatmapTransforms.Apply(BeatmapDocument.Parse(Map + "\n" + extra), Options(.8)));

    [Fact]
    public void Stats_RejectStaleOptionsAndMissingExplicitValues()
    {
        Assert.Throws<ArgumentException>(() => BeatmapTransforms.Apply(BeatmapDocument.Parse(Map.Replace("CircleSize:4", "CircleSize:3")), Options(.8)));
        var withoutAr = BeatmapDocument.Parse(Map.Replace("ApproachRate:7", "// omitted AR"));
        var current = BeatmapParser.ParseDocument(withoutAr);
        var options = new PracticeTransformOptions(1, 1, new(current.HP, current.CS, current.AR, current.OD), new(3, 3, 3, 3), PracticePitchPolicy.ChangeWithRate);
        Assert.Throws<NotSupportedException>(() => BeatmapTransforms.Apply(withoutAr, options));
    }

    [Theory]
    [InlineData("L")]
    [InlineData("P")]
    [InlineData("C")]
    public void Spacing_PreservesSupportedCurveTypes(string curve)
    {
        var result = BeatmapTransforms.Apply(BeatmapDocument.Parse(Map.Replace("B|", curve + "|")), Options(spacing: .5));
        Assert.Contains(curve + "|170:120|170:120|190:100,2,80", result.Document.ToString());
        Assert.False(result.RequiresAudioRendering);
    }
}
