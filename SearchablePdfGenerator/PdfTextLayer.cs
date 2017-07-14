// ------------------------------------------------------------------------------------
// <copyright file="PdfTextLayer.cs" company="Atalasoft">
//     (c) 2000-2017 Atalasoft, a Kofax Company. All rights reserved. Use is subject to license terms.
// </copyright>
// ------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private bool _overrideColor;
        private Color _currentColor;
        private Color _userColor;
        private SizeF _pageSize;

        private readonly Dictionary<Font, string> _fontsDictionary = new Dictionary<Font, string>();
        private readonly Dictionary<Font, bool> _unicodeRequired = new Dictionary<Font, bool>();
        private readonly Dictionary<PdfTextLine, PointF> _dimentions = new Dictionary<PdfTextLine, PointF>();

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
            
            CollectFonts(resources);
            SetHorizontalScaling(_dimentions, resources);
        }

        private void CollectFonts(GlobalResources resources)
        {
            Font currentFont = null;
            var prevPolicy = resources.Fonts.EmbeddingPolicyProvider;
            // this property is for decision-making based on permissions.
            // but we use source System.Font to decide whether to embed the font or not.
            resources.Fonts.EmbeddingPolicyProvider = (resource, permissions) =>
            {
                // at the moment of execution, the resource is not associated with a name or with System.Font.
                // so we use the currentFont value, setted below in foreach()
                PdfFontEmbeddingAction action;
                if (currentFont == null)
                    action = PdfFontEmbeddingAction.Embed;
                else
                    action = _unicodeRequired[currentFont]
                        ? PdfFontEmbeddingAction.Embed
                        : PdfFontEmbeddingAction.DontEmbed;
                return new PdfFontEmbeddingPolicy(action);
            };
            foreach (var font in _fontsDictionary)
            {
                currentFont = font.Key;
                var pdfFont = resources.Fonts.FromFont(font.Key); //here calls EmbeddingPolicyProvider
                resources.Fonts.Add(font.Value, pdfFont); 
            }
            resources.Fonts.EmbeddingPolicyProvider = prevPolicy;
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

            CreateTextShape(word.Text, word.Bounds, baseLine, resolution, ocrSize, rotation);
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

                CreateTextShape(glyph.Text, glyph.Bounds, baseLine, resolution, ocrSize, rotation);
            }
        }

        private void CreateTextShape(string text, Rectangle bounds, int baseLine, Dpi resolution, Size ocrSize, OcrTextRotation rotation)
        {
            _unicodeRequired[_currentFont] |= !IsPdfDocEncoding(text);
            Point p = GetTextOrigin(bounds, baseLine, ocrSize, rotation);
            PointF pf = PdfUnitConvertor.ConvertPagePointToPdf(p, resolution, _pageSize.Width, _pageSize.Height);

            var textShape = new PdfTextLine(_fontsDictionary[_currentFont], _currentFont.SizeInPoints, text,
                new PdfPoint(pf.X, pf.Y))
            {
                Rotation = (double) rotation,
                RenderMode = RenderMode,
                FillColor = PdfColorFactory.FromColor(_currentColor)
            };

            Shapes.Add(textShape);

            if (EnforceWordWidths)
            {
                PointF pageDims = GetRotatedDimensions(bounds, resolution, _pageSize.Width, _pageSize.Height,
                    rotation);
                _dimentions.Add(textShape, pageDims);
                
            }
        }

        private void SetHorizontalScaling(Dictionary<PdfTextLine, PointF> dimentions, GlobalResources resources)
        {
            foreach (var shapeDim in dimentions)
            {
                var shape = shapeDim.Key;
                var textWidth = MeasureText(shape.Text, resources.Fonts[shape.FontName]);
                var dim = shapeDim.Value;
                if (textWidth > 0 && Math.Abs(textWidth - dim.X) > double.Epsilon)
                    shape.HorizontalScaling = dim.X * 100 / textWidth;
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

        private double MeasureText(string text, PdfFontResource font)
        {
            return font.Metrics.MeasureText(_currentFont.SizeInPoints, text).Y;
        }

        private static bool IsPdfDocEncoding(string s)
        {
            return s.All(IsPdfDocEncoding);
        }

        private static bool IsPdfDocEncoding(char c)
        {
            // the PDF spec says this is ISO Latin 1, which is ISO 8859-1, shown in a table here:
            // http://en.wikipedia.org/wiki/ISO/IEC_8859-1
            return (c >= 32 && c <= 126) ||
                    (c >= 160 && c <= 255);
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
            bool changeColor = !_overrideColor && !color.Equals(CurrentColor);
            bool changeFont = !font.Equals(_currentFont);

            if (changeFont)
            {
                if (!_fontsDictionary.ContainsKey(font))
                {
                    var resName = resources.Fonts.NextName();
                    _fontsDictionary.Add(font, resName);
                    _unicodeRequired.Add(font, false);
                }

                _currentFont = font;
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
