// ------------------------------------------------------------------------------------
// <copyright file="PdfUnitConvertor.cs" company="Atalasoft">
//     (c) 2000-2017 Atalasoft, a Kofax Company. All rights reserved. Use is subject to license terms.
// </copyright>
// ------------------------------------------------------------------------------------

using Atalasoft.Imaging;
using System.Drawing;

namespace SearchablePdfGenerator
{
    internal static class PdfUnitConvertor
    {
        public static PointF ConvertPagePointToPdf(Point p, Dpi resolution, float pageWidth, float pageHeight)
        {
            PointF loc = ConvertPointToPdf(p, resolution);
            return new PointF(loc.X, pageHeight - loc.Y);
        }

        public static PointF ConvertPointToPdf(Point p, Dpi resolution)
        {
            return new PointF((float)ConvertPixelsToPdfPoints(p.X, resolution.X, resolution.Units),
                (float)ConvertPixelsToPdfPoints(p.Y, resolution.Y, resolution.Units));
        }

        public static SizeF ConvertSizeToPdf(Size p, Dpi resolution)
        {
            return new SizeF((float)ConvertPixelsToPdfPoints(p.Width, resolution.X, resolution.Units),
                (float)ConvertPixelsToPdfPoints(p.Height, resolution.Y, resolution.Units));
        }

        public static double ConvertPixelsToPdfPoints(int size, double res, ResolutionUnit units)
        {
            if (units == ResolutionUnit.DotsPerCentimeters)
            {
                return size / (res * 2.54) * 72.0;
            }
            return size / res * 72.0;
        }
    }
}
