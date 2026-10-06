using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace AuraLauncher.Core;

/// <summary>
/// Построитель настоящей 3D-модели игрока Minecraft на базе кубоидов и Viewport3D.
/// Размеры кубоидов и сцены: 1 px = 1 единица.
/// Голова 8x8x8, тело 8x12x4, ноги 4x12x4, руки 4x12x4 (тонкие: 3x12x4).
/// Накладной слой скина увеличен на 0.5 px с каждой стороны.
/// </summary>
public static class SkinModel3DBuilder
{
    public static BitmapSource UpscaleNearestNeighbor(BitmapSource source, int factor = 8)
    {
        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int srcW = formatted.PixelWidth;
        int srcH = formatted.PixelHeight;
        int srcStride = srcW * 4;
        byte[] srcPixels = new byte[srcH * srcStride];
        formatted.CopyPixels(srcPixels, srcStride, 0);

        int dstW = srcW * factor;
        int dstH = srcH * factor;
        int dstStride = dstW * 4;
        byte[] dstPixels = new byte[dstH * dstStride];

        for (int y = 0; y < dstH; y++)
        {
            int srcY = y / factor;
            int srcRowOffset = srcY * srcStride;
            int dstRowOffset = y * dstStride;

            for (int x = 0; x < dstW; x++)
            {
                int srcX = x / factor;
                int srcPixelOffset = srcRowOffset + srcX * 4;
                int dstPixelOffset = dstRowOffset + x * 4;

                dstPixels[dstPixelOffset] = srcPixels[srcPixelOffset];         // B
                dstPixels[dstPixelOffset + 1] = srcPixels[srcPixelOffset + 1]; // G
                dstPixels[dstPixelOffset + 2] = srcPixels[srcPixelOffset + 2]; // R
                dstPixels[dstPixelOffset + 3] = srcPixels[srcPixelOffset + 3]; // A
            }
        }

        var result = BitmapSource.Create(
            dstW, dstH,
            96, 96,
            PixelFormats.Bgra32,
            null,
            dstPixels,
            dstStride);
        result.Freeze();
        return result;
    }

    public static Model3DGroup BuildPlayerModel(BitmapSource skinBmp, bool isSlim)
    {
        var rootGroup = new Model3DGroup();

        // 1. Освещение: AmbientLight + мягкий DirectionalLight, без блика
        var ambientLight = new AmbientLight(Color.FromRgb(165, 165, 165));
        var dirLight = new DirectionalLight(Color.FromRgb(100, 100, 100), new Vector3D(-1, -1.5, -2));
        rootGroup.Children.Add(ambientLight);
        rootGroup.Children.Add(dirLight);

        // 2. Подготовка текстуры и материала
        int texW = skinBmp.PixelWidth;
        int texH = skinBmp.PixelHeight;
        bool is64x32 = (texH == 32);

        var upscaledTexture = UpscaleNearestNeighbor(skinBmp, 8);
        var brush = new ImageBrush(upscaledTexture)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);

        var material = new DiffuseMaterial(brush);

        // Извлекаем пиксели исходной текстуры для проверки прозрачности накладного слоя
        var formattedBmp = new FormatConvertedBitmap(skinBmp, PixelFormats.Bgra32, null, 0);
        int stride = texW * 4;
        byte[] rawPixels = new byte[texH * stride];
        formattedBmp.CopyPixels(rawPixels, stride, 0);

        // Группа базовых частей (непрозрачные, рендерятся первыми)
        var baseGroup = new Model3DGroup();
        // Группа накладок (+0.5 px, рендерятся вторыми)
        var overlayGroup = new Model3DGroup();

        int armW = isSlim ? 3 : 4;
        double rightArmMinX = isSlim ? -7.0 : -8.0;
        double rightArmMaxX = -4.0;
        double leftArmMinX = 4.0;
        double leftArmMaxX = isSlim ? 7.0 : 8.0;

        // ==========================================
        // БАЗОВЫЕ ЧАСТИ
        // ==========================================
        // Голова: 8x8x8, начало (0,0)
        baseGroup.Children.Add(CreateCuboidModel(
            -4, 4, 8, 16, -4, 4,
            0, 0, 8, 8, 8,
            material, texW, texH));

        // Тело: 8x12x4, начало (16,16)
        baseGroup.Children.Add(CreateCuboidModel(
            -4, 4, -4, 8, -2, 2,
            16, 16, 8, 12, 4,
            material, texW, texH));

        // Правая рука: w=3 или 4, h=12, d=4, начало (40,16)
        baseGroup.Children.Add(CreateCuboidModel(
            rightArmMinX, rightArmMaxX, -4, 8, -2, 2,
            40, 16, armW, 12, 4,
            material, texW, texH));

        // Левая рука: w=3 или 4, h=12, d=4.
        // Если 64x64: начало (32,48). Если 64x32: зеркалит правую руку (40,16).
        if (is64x32)
        {
            baseGroup.Children.Add(CreateCuboidModel(
                leftArmMinX, leftArmMaxX, -4, 8, -2, 2,
                40, 16, armW, 12, 4,
                material, texW, texH, mirrorU: true));
        }
        else
        {
            baseGroup.Children.Add(CreateCuboidModel(
                leftArmMinX, leftArmMaxX, -4, 8, -2, 2,
                32, 48, armW, 12, 4,
                material, texW, texH));
        }

        // Правая нога: 4x12x4, начало (0,16)
        baseGroup.Children.Add(CreateCuboidModel(
            -4, 0, -16, -4, -2, 2,
            0, 16, 4, 12, 4,
            material, texW, texH));

        // Левая нога: 4x12x4.
        // Если 64x64: начало (16,48). Если 64x32: зеркалит правую ногу (0,16).
        if (is64x32)
        {
            baseGroup.Children.Add(CreateCuboidModel(
                0, 4, -16, -4, -2, 2,
                0, 16, 4, 12, 4,
                material, texW, texH, mirrorU: true));
        }
        else
        {
            baseGroup.Children.Add(CreateCuboidModel(
                0, 4, -16, -4, -2, 2,
                16, 48, 4, 12, 4,
                material, texW, texH));
        }

        // ==========================================
        // НАКЛАДНОЙ СЛОЙ (+0.5 px с каждой стороны)
        // ==========================================
        // Шляпа головы: начало (32,0)
        var hatModel = CreateCuboidModel(
            -4.5, 4.5, 7.5, 16.5, -4.5, 4.5,
            32, 0, 8, 8, 8,
            material, texW, texH,
            isOverlay: true, rawPixels: rawPixels, stride: stride);
        if (hatModel != null) overlayGroup.Children.Add(hatModel);

        if (!is64x32)
        {
            // Куртка тела: начало (16,32)
            var jacketModel = CreateCuboidModel(
                -4.5, 4.5, -4.5, 8.5, -2.5, 2.5,
                16, 32, 8, 12, 4,
                material, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride);
            if (jacketModel != null) overlayGroup.Children.Add(jacketModel);

            // Рукав правой руки: начало (40,32)
            var rightSleeve = CreateCuboidModel(
                rightArmMinX - 0.5, rightArmMaxX + 0.5, -4.5, 8.5, -2.5, 2.5,
                40, 32, armW, 12, 4,
                material, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride);
            if (rightSleeve != null) overlayGroup.Children.Add(rightSleeve);

            // Рукав левой руки: начало (48,48)
            var leftSleeve = CreateCuboidModel(
                leftArmMinX - 0.5, leftArmMaxX + 0.5, -4.5, 8.5, -2.5, 2.5,
                48, 48, armW, 12, 4,
                material, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride);
            if (leftSleeve != null) overlayGroup.Children.Add(leftSleeve);

            // Штанина правой ноги: начало (0,32)
            var rightPant = CreateCuboidModel(
                -4.5, 0.5, -16.5, -3.5, -2.5, 2.5,
                0, 32, 4, 12, 4,
                material, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride);
            if (rightPant != null) overlayGroup.Children.Add(rightPant);

            // Штанина левой ноги: начало (0,48)
            var leftPant = CreateCuboidModel(
                -0.5, 4.5, -16.5, -3.5, -2.5, 2.5,
                0, 48, 4, 12, 4,
                material, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride);
            if (leftPant != null) overlayGroup.Children.Add(leftPant);
        }

        rootGroup.Children.Add(baseGroup);
        rootGroup.Children.Add(overlayGroup);

        return rootGroup;
    }

    private static GeometryModel3D? CreateCuboidModel(
        double xMin, double xMax,
        double yMin, double yMax,
        double zMin, double zMax,
        int u, int v, int w, int h, int d,
        Material material,
        int texW, int texH,
        bool mirrorU = false,
        bool isOverlay = false,
        byte[]? rawPixels = null,
        int stride = 0)
    {
        var mesh = new MeshGeometry3D();

        // UV прямоугольники согласно ТЗ:
        // сверху (u+d, v, w, d)
        // снизу (u+d+w, v, w, d)
        // справа (u, v+d, d, h)
        // спереди (u+d, v+d, w, h)
        // слева (u+d+w, v+d, d, h)
        // сзади (u+d+w+d, v+d, w, h)

        int topX = u + d, topY = v, topW = w, topH = d;
        int botX = u + d + w, botY = v, botW = w, botH = d;
        int rightX = u, rightY = v + d, rightW = d, rightH = h;
        int frontX = u + d, frontY = v + d, frontW = w, frontH = h;
        int leftX = u + d + w, leftY = v + d, leftW = d, leftH = h;
        int backX = u + d + w + d, backY = v + d, backW = w, backH = h;

        bool ShouldAdd(int fx, int fy, int fw, int fh)
        {
            if (!isOverlay || rawPixels == null) return true;
            return !IsFaceAllTransparent(rawPixels, stride, fx, fy, fw, fh, texW, texH);
        }

        void AddFace(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal, int fx, int fy, int fw, int fh, bool flipHoriz)
        {
            if (!ShouldAdd(fx, fy, fw, fh)) return;

            int baseIdx = mesh.Positions.Count;

            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);

            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);

            double u0 = (double)fx / texW;
            double u1 = (double)(fx + fw) / texW;
            double v0 = (double)fy / texH;
            double v1 = (double)(fy + fh) / texH;

            double uLeft = flipHoriz ? u1 : u0;
            double uRight = flipHoriz ? u0 : u1;

            mesh.TextureCoordinates.Add(new Point(uLeft, v0));
            mesh.TextureCoordinates.Add(new Point(uRight, v0));
            mesh.TextureCoordinates.Add(new Point(uLeft, v1));
            mesh.TextureCoordinates.Add(new Point(uRight, v1));

            mesh.TriangleIndices.Add(baseIdx + 0);
            mesh.TriangleIndices.Add(baseIdx + 2);
            mesh.TriangleIndices.Add(baseIdx + 1);

            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 2);
            mesh.TriangleIndices.Add(baseIdx + 3);
        }

        // 1. Спереди (+Z): p0=top-left, p1=top-right, p2=bottom-left, p3=bottom-right
        AddFace(
            new Point3D(xMin, yMax, zMax),
            new Point3D(xMax, yMax, zMax),
            new Point3D(xMin, yMin, zMax),
            new Point3D(xMax, yMin, zMax),
            new Vector3D(0, 0, 1),
            frontX, frontY, frontW, frontH,
            mirrorU);

        // 2. Сзади (-Z): смотрим сзади -> xMax слева, xMin справа
        AddFace(
            new Point3D(xMax, yMax, zMin),
            new Point3D(xMin, yMax, zMin),
            new Point3D(xMax, yMin, zMin),
            new Point3D(xMin, yMin, zMin),
            new Vector3D(0, 0, -1),
            backX, backY, backW, backH,
            mirrorU);

        // 3. Справа (правая сторона персонажа при X=xMin, нормаль -X):
        // Если mirrorU (зеркальная левая конечность): берет текстуру Left
        if (mirrorU)
        {
            AddFace(
                new Point3D(xMin, yMax, zMin),
                new Point3D(xMin, yMax, zMax),
                new Point3D(xMin, yMin, zMin),
                new Point3D(xMin, yMin, zMax),
                new Vector3D(-1, 0, 0),
                leftX, leftY, leftW, leftH,
                true);
        }
        else
        {
            AddFace(
                new Point3D(xMin, yMax, zMin),
                new Point3D(xMin, yMax, zMax),
                new Point3D(xMin, yMin, zMin),
                new Point3D(xMin, yMin, zMax),
                new Vector3D(-1, 0, 0),
                rightX, rightY, rightW, rightH,
                false);
        }

        // 4. Слева (левая сторона персонажа при X=xMax, нормаль +X):
        // Если mirrorU: берет текстуру Right
        if (mirrorU)
        {
            AddFace(
                new Point3D(xMax, yMax, zMax),
                new Point3D(xMax, yMax, zMin),
                new Point3D(xMax, yMin, zMax),
                new Point3D(xMax, yMin, zMin),
                new Vector3D(1, 0, 0),
                rightX, rightY, rightW, rightH,
                true);
        }
        else
        {
            AddFace(
                new Point3D(xMax, yMax, zMax),
                new Point3D(xMax, yMax, zMin),
                new Point3D(xMax, yMin, zMax),
                new Point3D(xMax, yMin, zMin),
                new Vector3D(1, 0, 0),
                leftX, leftY, leftW, leftH,
                false);
        }

        // 5. Сверху (+Y): смотрим сверху -> zMin (зад) сверху, zMax (перед) снизу
        AddFace(
            new Point3D(xMin, yMax, zMin),
            new Point3D(xMax, yMax, zMin),
            new Point3D(xMin, yMax, zMax),
            new Point3D(xMax, yMax, zMax),
            new Vector3D(0, 1, 0),
            topX, topY, topW, topH,
            mirrorU);

        // 6. Снизу (-Y): смотрим снизу -> zMax (перед) сверху, zMin (зад) снизу
        AddFace(
            new Point3D(xMin, yMin, zMax),
            new Point3D(xMax, yMin, zMax),
            new Point3D(xMin, yMin, zMin),
            new Point3D(xMax, yMin, zMin),
            new Vector3D(0, -1, 0),
            botX, botY, botW, botH,
            mirrorU);

        if (mesh.Positions.Count == 0) return null;

        return new GeometryModel3D(mesh, material);
    }

    private static bool IsFaceAllTransparent(byte[] pixels, int stride, int x, int y, int w, int h, int texW, int texH)
    {
        for (int py = y; py < y + h; py++)
        {
            if (py < 0 || py >= texH) continue;
            int rowOffset = py * stride;
            for (int px = x; px < x + w; px++)
            {
                if (px < 0 || px >= texW) continue;
                int offset = rowOffset + px * 4;
                byte alpha = pixels[offset + 3];
                if (alpha > 8) return false;
            }
        }
        return true;
    }
}
