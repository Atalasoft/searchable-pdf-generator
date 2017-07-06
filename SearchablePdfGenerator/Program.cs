// ------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Atalasoft">
//     (c) 2000-2017 Atalasoft, a Kofax Company. All rights reserved. Use is subject to license terms.
// </copyright>
// ------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Atalasoft.dotImage.PdfDoc.Bridge;
using Atalasoft.Imaging;
using Atalasoft.Imaging.Codec;
using Atalasoft.Imaging.Codec.Pdf;
using Atalasoft.Ocr;
using Atalasoft.Ocr.GlyphReader;
using Atalasoft.Pdf.TextExtract;
using Atalasoft.PdfDoc.Examiner;
using Atalasoft.PdfDoc.Generating;
using Atalasoft.PdfDoc.Generating.ResourceHandling;
using Atalasoft.PdfDoc.Generating.ResourceHandling.Fonts;

namespace SearchablePdfGenerator
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            RegisteredDecoders.Decoders.Add(new PdfDecoder());
            RegisteredDecoders.Decoders.Add(new TiffDecoder());

            var loader = new GlyphReaderLoader();

            foreach (var arg in args)
            {
                if (File.Exists(arg))
                    GenerateSearchablePdf(arg, $"{Path.GetFileNameWithoutExtension(arg)}_searchable.pdf");
            }
        }

        private static PdfTextDocument _pdfTextDocument;

        private static void GenerateSearchablePdf(string srcPath, string outPath)
        {
            List<OcrPage> ocrPages;
            IFontBuilder fontBuilder;
            IFontMapper fontMapper;
            using (var engine = new GlyphReaderEngine())
            {
                engine.Initialize();
                fontMapper = engine.FontMapper;
                fontBuilder = engine.FontBuilder;

                ocrPages = Recognize(srcPath, engine);
            }

            if (GetIsPdf(srcPath))
                FormPdf(srcPath, outPath, ocrPages, fontMapper, fontBuilder);
            else
                FromImages(srcPath, outPath, ocrPages, fontMapper, fontBuilder);
        }

        private static void FromImages(string srcPath, string outPath, IList<OcrPage> ocrPages, IFontMapper fontMapper,
            IFontBuilder fontBuilder)
        {
            using (var genDoc = AtalaImageCompressor.CreateDocument())
            {
                using (var images = new FileSystemImageSource(srcPath, true))
                {
                    while (images.HasMoreImages())
                        using (var image = images.AcquireNext())
                            genDoc.AddPage(GeneratePdfPage(genDoc.Resources, image));

                    AddTextLayers(genDoc, ocrPages, fontMapper, fontBuilder, images);
                }
                genDoc.Save(outPath);
            }
        }

        private static void FormPdf(string srcPath, string outPath, IList<OcrPage> ocrPages, IFontMapper fontMapper,
            IFontBuilder fontBuilder)
        {
            using (var inStm = File.OpenRead(srcPath))
            using (_pdfTextDocument = new PdfTextDocument(inStm))
            using (var genDoc = new PdfGeneratedDocument(inStm))
            {
                genDoc.Resources.Images.Compressors.Add(new AtalaImageCompressor());
                using (var images = new FileSystemImageSource(srcPath, true))
                    AddTextLayers(genDoc, ocrPages, fontMapper, fontBuilder, images);

                genDoc.Save(outPath);
            }
        }

        private static void AddTextLayers(PdfGeneratedDocument genDoc, IList<OcrPage> ocrPages, IFontMapper fontMapper,
            IFontBuilder fontBuilder, RandomAccessImageSource images)
        {
            for (var index = 0; index < ocrPages.Count; index++)
            {
                var ocrPage = ocrPages[index];
                if (ocrPage == null)
                    continue;

                var pdfPage = genDoc.Pages[index] as PdfGeneratedPage;
                if (pdfPage == null)
                    continue;

                if (GetIsPageContainsChars(index))
                {
                    //rebuild content
                    pdfPage.DrawingList.Clear();
                    using (var image = images[index])
                    {
                        var imgShape = AtalaImageCompressor.CreateImageShape(genDoc.Resources, image);
                        pdfPage.DrawingList.AddShape(imgShape);
                    }
                }

                var textLayer = new PdfTextLayer(fontMapper, fontBuilder)
                {
                    RenderMode = PdfTextRenderMode.Invisible
                };
                textLayer.ProcessPage(ocrPage, genDoc.Resources);
                foreach (var shape in textLayer.Shapes)
                {
                    pdfPage.DrawingList.AddShape(shape);
                }
            }
        }

        private static PdfGeneratedPage GeneratePdfPage(GlobalResources resources, AtalaImage image)
        {
            var size = PdfUnitConvertor.ConvertSizeToPdf(new Size(image.Width, image.Height), image.Resolution);
            var page = new PdfGeneratedPage(size.Width, size.Height);
            var imgShape = AtalaImageCompressor.CreateImageShape(resources, image);
            page.DrawingList.AddShape(imgShape);
            return page;
        }

        private static bool GetIsPdf(string srcPath)
        {
            using (var pdfStm = File.OpenRead(srcPath))
            {
                var res = ExaminerResults.FromStream(pdfStm, null, null);
                return res.IsPdf;
            }
        }

        private static bool GetIsPageContainsChars(int index)
        {
            if (_pdfTextDocument == null)
                return false;
            return _pdfTextDocument.GetPage(index).CharCount != 0;
        }

        private static List<OcrPage> Recognize(string path, OcrEngine engine)
        {
            using (var images = new FileSystemImageSource(path, true))
            {
                var ocrPages = new List<OcrPage>();
                while (images.HasMoreImages())
                    using (var image = images.AcquireNext())
                        ocrPages.Add(engine.Recognize(image));
                return ocrPages;
            }
        }
    }
}
