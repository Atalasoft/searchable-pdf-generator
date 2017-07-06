// ------------------------------------------------------------------------------------
// <copyright file="PdfTextLayer.cs" company="Atalasoft">
//     (c) 2000-2017 Atalasoft, a Kofax Company. All rights reserved. Use is subject to license terms.
// </copyright>
// ------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Atalasoft.Imaging;
using Atalasoft.Ocr;
using Atalasoft.PdfDoc.Generating;
using Atalasoft.PdfDoc.Generating.ResourceHandling;
using Atalasoft.PdfDoc.Generating.ResourceHandling.Fonts;
using Atalasoft.PdfDoc.Generating.Shapes;
using Atalasoft.PdfDoc.Geometry;

namespace SearchablePdfGenerator
{
    public class PdfTextLayer
    {
        private readonly IFontMapper _fontMapper;
        private readonly IFontBuilder _fontBuilder;


        private Font _currentFont;
        private PdfFontResource _currentPdfFont;
        private bool _overrideColor;
        private Color _currentColor;
        private Color _userColor;
        private SizeF _pageSize;

        private readonly Dictionary<Font, string> _fontsDictionary = new Dictionary<Font, string>();

        public SizeF LayerSize { get { return _pageSize; } set { _pageSize = value; } }

        public IList<PdfBaseShape> Shapes { get; private set; }

        public PdfTextRenderMode RenderMode { get; set; }

        public bool UseNormalizedBaseline { get; set; }

        public bool EnforceWordWidths { get; set; }

        public PdfTextLayer(IFontMapper fontMapper, IFontBuilder fontBuilder)
        {
            Shapes = new List<PdfBaseShape>();
            _fontMapper = fontMapper;
            _fontBuilder = fontBuilder;
            EnforceWordWidths = true;
            UseNormalizedBaseline = true;
        }

        public void ProcessPage(OcrPage page, GlobalResources resources)
        {
            var info = GetPageInfo(page);
            _pageSize = info?.PdfPageSize ?? PdfUnitConvertor.ConvertSizeToPdf(new Size(page.Width, page.Height), page.Resolution);

            _currentFont = null;
            _overrideColor = !info?.UseDocumentTextColor ?? false;
            _currentColor = Color.Black;
            if (_overrideColor && info != null)
                _currentColor = _userColor = info.TextColor;

            LayoutRegions(page.Regions, resources, page.Resolution, new Size(page.Width, page.Height));
        }

        private void LayoutRegions(IEnumerable regions, GlobalResources resources, Dpi resolution, Size ocrSize)
        {
            foreach (OcrRegion region in regions)
            {
                var textRegion = region as OcrTextRegion;
                if (textRegion != null)
                {
                    foreach (OcrLine line in textRegion.Lines)
                    {
                        if (line.Text.Length == 0)
                            continue;
                        if (line.StyleIsUniform(_fontMapper, _fontBuilder))
                            HandleUniformLine(line, resources, resolution, ocrSize, textRegion.Rotation);
                        else
                            HandleNonUniformLine(line, resources, resolution, ocrSize, textRegion.Rotation);
                    }
                    continue;
                }

                OcrTableRegion tableRegion = region as OcrTableRegion;
                if (tableRegion != null)
                    LayoutRegions(tableRegion.Cells, resources, resolution, ocrSize);
            }
        }


        private void HandleUniformLine(OcrLine line, GlobalResources resources, Dpi resolution, Size ocrSize,
            OcrTextRotation rotation)
        {
            if (line.Text.Length == 0)
                return;
            using (Font gdiFont = line.GetFontAt(resolution, _fontMapper, _fontBuilder, 0))
            {
                Color color = line.GetFontColorAt(0);
                SetFontAndColor(gdiFont, color, resources);
            }

            int baseLine = line.Baseline;
            foreach (OcrWord word in line.Words)
            {
                HandleUniformWord(word, baseLine, resolution, ocrSize, rotation);
            }

        }

        private void HandleNonUniformLine(OcrLine line, GlobalResources resources, Dpi resolution, Size ocrSize, OcrTextRotation rotation)
        {
            if (line.Text.Length == 0)
                return;
            int baseLine = line.Baseline;
            foreach (OcrWord word in line.Words)
            {
                if (word.StyleIsUniform(_fontMapper, _fontBuilder))
                {
                    using (Font gdiFont = word.GetFontAt(resolution, _fontMapper, _fontBuilder, 0))
                    {
                        Color color = word.GetFontColorAt(0);
                        SetFontAndColor(gdiFont, color, resources);
                    }

                    HandleUniformWord(word, baseLine, resolution, ocrSize, rotation);
                }
                else
                {
                    HandleNonUniformWord(word, baseLine, resources, resolution, ocrSize, rotation);
                }
            }
        }

        private void HandleUniformWord(OcrWord word, int baseLine, Dpi resolution, Size ocrSize,
            OcrTextRotation rotation)
        {
            if (word.Text.Length == 0)
                return;

            baseLine = GetWordBaseline(word, baseLine, resolution);

            Point p = GetTextOrigin(word.Bounds, baseLine, ocrSize, rotation);
            PointF pf = PdfUnitConvertor.ConvertPagePointToPdf(p, resolution, _pageSize.Width, _pageSize.Height);

            var text = new PdfTextLine(_fontsDictionary[_currentFont], _currentFont.SizeInPoints, word.Text,
                new PdfPoint(pf.X, pf.Y))
            {
                Rotation = (double)rotation,
                RenderMode = RenderMode,
                FillColor = PdfColorFactory.FromColor(_currentColor)
            };

            Shapes.Add(text);

            if (EnforceWordWidths)
            {
                PointF pageDims = GetRotatedDimensions(word.Bounds, resolution, _pageSize.Width, _pageSize.Height,
                    rotation);
                double textWidth = MeasureText(word.Text);
                if (textWidth > 0)
                    text.HorizontalScaling = pageDims.X * 100 / textWidth;
            }
        }

        private void HandleNonUniformWord(OcrWord word, int baseLine, GlobalResources resources, Dpi resolution, Size ocrSize, OcrTextRotation rotation)
        {
            if (word.Text.Length == 0)
                return;

            baseLine = GetWordBaseline(word, baseLine, resolution);
            foreach (OcrGlyph glyph in word.Glyphs)
            {
                using (Font gdiFont = glyph.GetFont(resolution, _fontMapper, _fontBuilder))
                {
                    Color color = glyph.Color;
                    SetFontAndColor(gdiFont, color, resources);
                }

                Point p = GetTextOrigin(glyph.Bounds, baseLine, ocrSize, rotation);
                PointF pf = PdfUnitConvertor.ConvertPagePointToPdf(p, resolution, _pageSize.Width, _pageSize.Height);

                var text = new PdfTextLine(_fontsDictionary[_currentFont], _currentFont.SizeInPoints, word.Text,
                    new PdfPoint(pf.X, pf.Y))
                {
                    Rotation = (double)rotation,
                    RenderMode = RenderMode,
                    FillColor = PdfColorFactory.FromColor(_currentColor)
                };

                Shapes.Add(text);

                if (EnforceWordWidths)
                {
                    PointF pageDims = GetRotatedDimensions(glyph.Bounds, resolution, _pageSize.Width, _pageSize.Height, rotation);
                    double textWidth = MeasureText(glyph.Text);
                    if (textWidth > 0 && Math.Abs(textWidth - pageDims.X) > double.Epsilon)
                        text.HorizontalScaling = pageDims.X * 100.0 / textWidth;
                }
            }
        }


        private int GetWordBaseline(OcrWord word, int baseLine, Dpi resolution)
        {
            if (word.Text.Length == 0)
                return baseLine;

            if (UseNormalizedBaseline)
            {
                return baseLine;
            }
            int wordBaseLine = word.Baseline;
            int absBaseLineDelta = Math.Abs(baseLine - wordBaseLine);
            Font wordFont = word.GetFontAt(resolution, _fontMapper, _fontBuilder, 0);

            // heuristic for when to honor the word's baseline:
            // 1. the delta for the baseline is greater than 1/4 of the word's bounding box height (sub or super)
            // 2. the point size is great than 2 points different (probably sub/super)
            if (absBaseLineDelta > word.Bounds.Height / 4 || (_currentFont != null && Math.Abs(_currentFont.SizeInPoints - wordFont.SizeInPoints) > 2.0))
            {
                return wordBaseLine;
            }
            return baseLine;
        }

        private double MeasureText(string text)
        {
            return _currentPdfFont.Metrics.MeasureText(_currentFont.SizeInPoints, text).Y;
        }

        private Point GetTextOrigin(Rectangle r, int baseline, Size ocrSize, OcrTextRotation rotation)
        {
            switch (rotation)
            {
                default:
                    return new Point(r.Left, baseline);
                case OcrTextRotation.Clockwise90:
                    return new Point(ocrSize.Width - baseline, r.Top);
                case OcrTextRotation.Clockwise180:
                    return new Point(r.Right, ocrSize.Height - baseline);
                case OcrTextRotation.Clockwise270:
                    return new Point(baseline, r.Bottom);
            }
        }

        private PointF GetRotatedDimensions(Rectangle r, Dpi resolution, float pdfPageWidth, float pdfPageHeight, OcrTextRotation rotation)
        {
            Point p = new Point(r.Width, r.Height);
            switch (rotation)
            {
                case OcrTextRotation.None:
                    break;
                case OcrTextRotation.Clockwise180:
                    break;
                case OcrTextRotation.Clockwise90:
                case OcrTextRotation.Clockwise270:
                    p = new Point(r.Height, r.Width);
                    break;
            }
            return PdfUnitConvertor.ConvertPagePointToPdf(p, resolution, pdfPageWidth, pdfPageHeight);
        }


        private void SetFontAndColor(Font font, Color color, GlobalResources resources)
        {
            bool changeColor = !_overrideColor && !(color.Equals(CurrentColor));
            bool changeFont = !font.Equals(_currentFont);

            if (changeFont)
            {
                if (!_fontsDictionary.ContainsKey(font))
                {
                    _currentPdfFont = resources.Fonts.FromFont(font);
                    var resName = resources.Fonts.Add(_currentPdfFont);
                    _fontsDictionary.Add(font, resName);
                }

                _currentFont = font;
                _currentPdfFont = resources.Fonts.Get(_fontsDictionary[font]);
            }

            if (changeColor)
                CurrentColor = color;
        }

        private Color CurrentColor
        {
            get { return _overrideColor ? _userColor : _currentColor; }
            set
            {
                if (!_overrideColor)
                    _currentColor = value;
            }
        }

        private static PdfPageInfo GetPageInfo(OcrPage page)
        {
            var metadata = page.Metadata[OcrPageMetadataKey.PdfPageInfo];
            if (metadata == null)
                return null;
            var pageData = metadata as Hashtable;
            if (pageData == null)
                throw new OcrException("Expecting PdfPageInfo metadata, but found an incompatible type.");
            var infodata = pageData["PageInfo"] as PdfPageInfo;
            if (infodata == null)
                throw new OcrException("Expecting PdfPageInfo object, but found null or an incompatible type.");
            return infodata;
        }
    }
}
