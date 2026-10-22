// Images into PDFs and PDFs into pictures: the PdfSharp and PDFium converters, and the merge / split the
// JobQueue does with them. Inputs are generated in the tests.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using PdfSharp.Pdf.IO;

namespace Filee.Engines.Tests;

public class ImageAndPdfTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static void AssertDone(ConversionJob job)
    {
        var errors = string.Join("; ", job.Files.Where(f => f.State == FileState.Failed).Select(f => $"{f.ErrorKey} {f.ErrorDetail}"));
        Assert.True(job.State == JobState.Completed, $"{job.State}: {errors}");
        Assert.All(job.Outputs, o => Assert.True(new FileInfo(o).Length > 0, o));
    }

    [Fact]
    public async Task Images_to_pdf_and_back()
    {
        var dir = fx.NewFolder();
        var a = EngineFixture.MakeImage(dir, "1.png", "png");
        var b = EngineFixture.MakeImage(dir, "2.png", "png");

        var toPdf = await fx.ConvertAsync([a, b], new Preset { TargetFormat = "pdf", Pdf = { MergeIntoSingle = true } });
        AssertDone(toPdf);
        using (var pdf = PdfReader.Open(toPdf.Outputs.Single(), PdfDocumentOpenMode.Import))
            Assert.Equal(2, pdf.PageCount);

        var toPng = await fx.ConvertAsync([toPdf.Outputs.Single()], new Preset { TargetFormat = "png", Pdf = { RenderDpi = 72 }, Output = { FileNamePattern = "{name}_render" } });
        AssertDone(toPng);
        Assert.Equal(2, toPng.Outputs.Count());
    }

    [Fact]
    public async Task Merge_images_into_one_pdf()
    {
        var dir = fx.NewFolder();
        var a = EngineFixture.MakeImage(dir, "1.jpg", "jpg");
        var b = EngineFixture.MakeImage(dir, "2.png", "png");
        var c = EngineFixture.MakeImage(dir, "3.webp", "webp");

        var job = await fx.ConvertAsync([a, b, c], new Preset { TargetFormat = "pdf", Pdf = { MergeIntoSingle = true, PageSize = PdfPageSize.A4, MarginMm = 10 }, Output = { FileNamePattern = "{name}_merged" } });

        AssertDone(job);
        using var pdf = PdfReader.Open(job.Outputs.Single(), PdfDocumentOpenMode.Import);
        Assert.Equal(3, pdf.PageCount);
        Assert.Equal("1_merged.pdf", Path.GetFileName(job.Outputs.Single()));
    }

    [Fact]
    public async Task Split_pdf_by_page_range()
    {
        var dir = fx.NewFolder();
        var pdf = EngineFixture.MakePdf(dir, "doc.pdf", 4);

        var job = await fx.ConvertAsync([pdf], new Preset
        {
            TargetFormat = "pdf",
            Pdf = { SplitPages = true, PageRange = "2-3" },
            Output = { Location = OutputLocation.Subfolder, SubfolderName = "{name}" },
        });

        AssertDone(job);
        Assert.Equal(["doc_p2.pdf", "doc_p3.pdf"], job.Outputs.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_pdf_renders_one_picture_per_page()
    {
        var dir = fx.NewFolder();
        var pdf = EngineFixture.MakePdf(dir, "doc.pdf", 3);

        var job = await fx.ConvertAsync([pdf], new Preset { TargetFormat = "png", Pdf = { RenderDpi = 72 } });

        AssertDone(job);
        Assert.Equal(["doc_p1.png", "doc_p2.png", "doc_p3.png"], job.Outputs.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_page_range_renders_only_those_pages()
    {
        var dir = fx.NewFolder();
        var pdf = EngineFixture.MakePdf(dir, "doc.pdf", 5);

        var job = await fx.ConvertAsync([pdf], new Preset { TargetFormat = "jpg", Pdf = { RenderDpi = 72, PageRange = "2,4" } });

        AssertDone(job);
        Assert.Equal(["doc_p2.jpg", "doc_p4.jpg"], job.Outputs.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_PDF_is_not_copied_as_if_rotated_it_fails_and_says_why()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakePdf(dir, "doc.pdf", 1);

        var job = await fx.ConvertAsync([source], BuiltInData.CreatePresets().Single(p => p.Id == "rotate-left"));

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("error.cannot_turn", job.Files.Single().ErrorKey);
        Assert.False(File.Exists(Path.Combine(dir, "doc_left.pdf")));
    }

    [Fact]
    public async Task An_image_into_a_PDF_is_turned_on_the_way()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "wide.png", "png", 400, 200);
        var preset = BuiltInData.CreatePresets().Single(p => p.Id == "rotate-left");
        preset.TargetFormat = "pdf";

        var job = await fx.ConvertAsync([source], preset);

        Assert.Equal(JobState.Completed, job.State);
        using var pdf = PdfReader.Open(job.Outputs.Single(), PdfDocumentOpenMode.Import);
        Assert.True(pdf.Pages[0].Height.Point > pdf.Pages[0].Width.Point); // landscape image, portrait page
    }
}
