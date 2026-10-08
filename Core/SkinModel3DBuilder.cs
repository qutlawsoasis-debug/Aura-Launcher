using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace AuraLauncher.Core;

public enum PlayerLimbPart
{
    None = 0,
    Head,
    Body,
    RightArm,
    LeftArm,
    RightLeg,
    LeftLeg
}

/// <summary>
/// Построитель настоящей 3D-модели игрока Minecraft на базе кубоидов и Viewport3D.
/// Размеры кубоидов и сцены: 1 px = 1 единица.
/// Голова 8x8x8, тело 8x12x4, ноги 4x12x4, руки 4x12x4 (тонкие: 3x12x4).
/// Накладной слой скина увеличен на 0.5 px с каждой стороны.
/// </summary>
public static class SkinModel3DBuilder
{
    public static readonly DependencyProperty LimbPartProperty =
        DependencyProperty.RegisterAttached(
            "LimbPart",
            typeof(PlayerLimbPart),
            typeof(SkinModel3DBuilder),
            new PropertyMetadata(PlayerLimbPart.None));

    public static readonly DependencyProperty IsSlimModelProperty =
        DependencyProperty.RegisterAttached(
            "IsSlimModel",
            typeof(bool),
            typeof(SkinModel3DBuilder),
            new PropertyMetadata(false));

    public static PlayerLimbPart GetLimbPart(DependencyObject obj) => (PlayerLimbPart)obj.GetValue(LimbPartProperty);
    public static void SetLimbPart(DependencyObject obj, PlayerLimbPart value) => obj.SetValue(LimbPartProperty, value);

    public static bool GetIsSlimModel(DependencyObject obj) => (bool)obj.GetValue(IsSlimModelProperty);
    public static void SetIsSlimModel(DependencyObject obj, bool value) => obj.SetValue(IsSlimModelProperty, value);

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

        // 1. Освещение: Ambient ~70% + один мягкий Directional сверху-спереди (белый скин не выглядит серым)
        var ambientLight = new AmbientLight(Color.FromRgb(180, 180, 180));
        var dirLight = new DirectionalLight(Color.FromRgb(95, 95, 95), new Vector3D(0.2, -1.0, -1.2));
        rootGroup.Children.Add(ambientLight);
        rootGroup.Children.Add(dirLight);

        // 2. Подготовка текстуры и материала
        int texW = skinBmp.PixelWidth;
        int texH = skinBmp.PixelHeight;
        bool is64x32 = (texH == 32);

        // Извлекаем пиксели исходной текстуры для проверки прозрачности накладного слоя
        var formattedBmp = new FormatConvertedBitmap(skinBmp, PixelFormats.Bgra32, null, 0);
        int stride = texW * 4;
        byte[] rawPixels = new byte[texH * stride];
        formattedBmp.CopyPixels(rawPixels, stride, 0);

        // Подготовка базового слоя:
        // В Minecraft базовый слой всегда на 100% непрозрачен (Alpha = 255).
        // Если скин формата Alex (рука 3px) отображается в обычном режиме (рука 4px),
        // в скине Alex колонки 54-55 (правая рука) и 46-47 (левая рука) имеют прозрачность.
        // Заполняем эти краевые колонки цветом соседних пикселей (53 и 45), чтобы на задней грани руки не было дыр.
        byte[] basePixels = (byte[])rawPixels.Clone();
        if (!isSlim)
        {
            for (int y = 16; y < Math.Min(32, texH); y++)
            {
                int rOffset = y * stride;
                if (basePixels[rOffset + 54 * 4 + 3] == 0)
                {
                    for (int c = 54; c <= 55; c++)
                    {
                        basePixels[rOffset + c * 4 + 0] = basePixels[rOffset + 53 * 4 + 0];
                        basePixels[rOffset + c * 4 + 1] = basePixels[rOffset + 53 * 4 + 1];
                        basePixels[rOffset + c * 4 + 2] = basePixels[rOffset + 53 * 4 + 2];
                        basePixels[rOffset + c * 4 + 3] = 255;
                    }
                }
            }
            if (!is64x32)
            {
                for (int y = 48; y < Math.Min(64, texH); y++)
                {
                    int rOffset = y * stride;
                    if (basePixels[rOffset + 46 * 4 + 3] == 0)
                    {
                        for (int c = 46; c <= 47; c++)
                        {
                            basePixels[rOffset + c * 4 + 0] = basePixels[rOffset + 45 * 4 + 0];
                            basePixels[rOffset + c * 4 + 1] = basePixels[rOffset + 45 * 4 + 1];
                            basePixels[rOffset + c * 4 + 2] = basePixels[rOffset + 45 * 4 + 2];
                            basePixels[rOffset + c * 4 + 3] = 255;
                        }
                    }
                }
            }
        }

        // Принудительно делаем непрозрачными базовые области
        for (int y = 0; y < texH; y++)
        {
            int rOffset = y * stride;
            for (int x = 0; x < texW; x++)
            {
                bool isBaseArea = (y < 16 && x < 32) ||
                                  (y >= 16 && y < 32) ||
                                  (y >= 48 && y < 64 && x >= 16 && x < 48);
                if (isBaseArea)
                {
                    basePixels[rOffset + x * 4 + 3] = 255;
                }
            }
        }

        var baseBmp = BitmapSource.Create(texW, texH, 96, 96, PixelFormats.Bgra32, null, basePixels, stride);
        baseBmp.Freeze();

        var baseUpscaled = UpscaleNearestNeighbor(baseBmp, 8);
        var baseBrush = new ImageBrush(baseUpscaled)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(baseBrush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(baseBrush, EdgeMode.Aliased);
        if (baseBrush.CanFreeze) baseBrush.Freeze();
        var baseMaterial = new DiffuseMaterial(baseBrush);
        if (baseMaterial.CanFreeze) baseMaterial.Freeze();

        var overlayUpscaled = UpscaleNearestNeighbor(skinBmp, 8);
        var overlayBrush = new ImageBrush(overlayUpscaled)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(overlayBrush, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(overlayBrush, EdgeMode.Aliased);
        if (overlayBrush.CanFreeze) overlayBrush.Freeze();
        var overlayMaterial = new DiffuseMaterial(overlayBrush);
        if (overlayMaterial.CanFreeze) overlayMaterial.Freeze();

        // Группа базовых частей (непрозрачные, рендерятся первыми)
        var baseGroup = new Model3DGroup();
        // Группа накладок (+0.5 px, рендерятся вторыми)
        var overlayGroup = new Model3DGroup();

        int armW = isSlim ? 3 : 4;
        double rightArmMinX = isSlim ? -7.0 : -8.0;
        double rightArmMaxX = -4.0;
        double leftArmMinX = 4.0;
        double leftArmMaxX = isSlim ? 7.0 : 8.0;

        // Базовые части (baseMaterial)
        // Голова: 8x8x8, начало (0,0)
        baseGroup.Children.Add(CreateCuboidModel(
            -4, 4, 8, 16, -4, 4,
            0, 0, 8, 8, 8,
            baseMaterial, texW, texH, limbPart: PlayerLimbPart.Head));

        // Тело: 8x12x4, начало (16,16)
        baseGroup.Children.Add(CreateCuboidModel(
            -4, 4, -4, 8, -2, 2,
            16, 16, 8, 12, 4,
            baseMaterial, texW, texH, limbPart: PlayerLimbPart.Body));

        // Правая рука: w=3 или 4, h=12, d=4, начало (40,16)
        baseGroup.Children.Add(CreateCuboidModel(
            rightArmMinX, rightArmMaxX, -4, 8, -2, 2,
            40, 16, armW, 12, 4,
            baseMaterial, texW, texH, limbPart: PlayerLimbPart.RightArm));

        // Левая рука: w=3 или 4, h=12, d=4.
        // Если 64x64: начало (32,48). Если 64x32: зеркалит правую руку (40,16).
        if (is64x32)
        {
            baseGroup.Children.Add(CreateCuboidModel(
                leftArmMinX, leftArmMaxX, -4, 8, -2, 2,
                40, 16, armW, 12, 4,
                baseMaterial, texW, texH, mirrorU: true, limbPart: PlayerLimbPart.LeftArm));
        }
        else
        {
            baseGroup.Children.Add(CreateCuboidModel(
                leftArmMinX, leftArmMaxX, -4, 8, -2, 2,
                32, 48, armW, 12, 4,
                baseMaterial, texW, texH, limbPart: PlayerLimbPart.LeftArm));
        }

        // Правая нога: 4x12x4, начало (0,16)
        baseGroup.Children.Add(CreateCuboidModel(
            -4, 0, -16, -4, -2, 2,
            0, 16, 4, 12, 4,
            baseMaterial, texW, texH, limbPart: PlayerLimbPart.RightLeg));

        // Левая нога: 4x12x4.
        // Если 64x64: начало (16,48). Если 64x32: зеркалит правую ногу (0,16).
        if (is64x32)
        {
            baseGroup.Children.Add(CreateCuboidModel(
                0, 4, -16, -4, -2, 2,
                0, 16, 4, 12, 4,
                baseMaterial, texW, texH, mirrorU: true, limbPart: PlayerLimbPart.LeftLeg));
        }
        else
        {
            baseGroup.Children.Add(CreateCuboidModel(
                0, 4, -16, -4, -2, 2,
                16, 48, 4, 12, 4,
                baseMaterial, texW, texH, limbPart: PlayerLimbPart.LeftLeg));
        }

        // Накладной слой (+0.5 px с каждой стороны, overlayMaterial)
        // Шляпа головы: начало (32,0)
        var hatModel = CreateCuboidModel(
            -4.5, 4.5, 7.5, 16.5, -4.5, 4.5,
            32, 0, 8, 8, 8,
            overlayMaterial, texW, texH,
            isOverlay: true, rawPixels: rawPixels, stride: stride, limbPart: PlayerLimbPart.Head);
        if (hatModel != null) overlayGroup.Children.Add(hatModel);

        if (!is64x32)
        {
            // Куртка тела: начало (16,32)
            var jacketModel = CreateCuboidModel(
                -4.5, 4.5, -4.5, 8.5, -2.5, 2.5,
                16, 32, 8, 12, 4,
                overlayMaterial, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride, limbPart: PlayerLimbPart.Body);
            if (jacketModel != null) overlayGroup.Children.Add(jacketModel);

            // Рукав правой руки: начало (40,32)
            var rightSleeve = CreateCuboidModel(
                rightArmMinX - 0.5, rightArmMaxX + 0.5, -4.5, 8.5, -2.5, 2.5,
                40, 32, armW, 12, 4,
                overlayMaterial, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride, limbPart: PlayerLimbPart.RightArm);
            if (rightSleeve != null) overlayGroup.Children.Add(rightSleeve);

            // Рукав левой руки: начало (48,48)
            var leftSleeve = CreateCuboidModel(
                leftArmMinX - 0.5, leftArmMaxX + 0.5, -4.5, 8.5, -2.5, 2.5,
                48, 48, armW, 12, 4,
                overlayMaterial, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride, limbPart: PlayerLimbPart.LeftArm);
            if (leftSleeve != null) overlayGroup.Children.Add(leftSleeve);

            // Штанина правой ноги: начало (0,32)
            var rightPant = CreateCuboidModel(
                -4.5, 0.5, -16.5, -3.5, -2.5, 2.5,
                0, 32, 4, 12, 4,
                overlayMaterial, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride, limbPart: PlayerLimbPart.RightLeg);
            if (rightPant != null) overlayGroup.Children.Add(rightPant);

            // Штанина левой ноги: начало (0,48)
            var leftPant = CreateCuboidModel(
                -0.5, 4.5, -16.5, -3.5, -2.5, 2.5,
                0, 48, 4, 12, 4,
                overlayMaterial, texW, texH,
                isOverlay: true, rawPixels: rawPixels, stride: stride, limbPart: PlayerLimbPart.LeftLeg);
            if (leftPant != null) overlayGroup.Children.Add(leftPant);
        }

        if (baseGroup.CanFreeze) baseGroup.Freeze();
        if (overlayGroup.CanFreeze) overlayGroup.Freeze();
        rootGroup.Children.Add(baseGroup);
        rootGroup.Children.Add(overlayGroup);
        SetIsSlimModel(rootGroup, isSlim);
        if (rootGroup.CanFreeze) rootGroup.Freeze();

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
        int stride = 0,
        PlayerLimbPart limbPart = PlayerLimbPart.None)
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

        // 6. Снизу (-Y): в текстуре v0 = zMin (зад), v1 = zMax (перед)
        AddFace(
            new Point3D(xMin, yMin, zMin),
            new Point3D(xMax, yMin, zMin),
            new Point3D(xMin, yMin, zMax),
            new Point3D(xMax, yMin, zMax),
            new Vector3D(0, -1, 0),
            botX, botY, botW, botH,
            mirrorU);

        if (mesh.Positions.Count == 0) return null;
        if (mesh.CanFreeze) mesh.Freeze();

        var geomModel = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };
        if (limbPart != PlayerLimbPart.None)
        {
            SetLimbPart(geomModel, limbPart);
        }
        if (geomModel.CanFreeze) geomModel.Freeze();
        return geomModel;
    }

    /// <summary>
    /// Диагностический метод: строит 3D-модель игрока, где каждая грань каждого кубоида
    /// покрашена в свой уникальный цвет. Задает и Material, и BackMaterial.
    /// Позволяет визуально проверить отсутствие дыр и перекрытий со всех ракурсов.
    /// </summary>
    public static Model3DGroup BuildDiagnosticPlayerModel(bool isSlim)
    {
        var rootGroup = new Model3DGroup();
        rootGroup.Children.Add(new AmbientLight(Color.FromRgb(210, 210, 210)));
        rootGroup.Children.Add(new DirectionalLight(Color.FromRgb(80, 80, 80), new Vector3D(0.2, -1.0, -1.2)));

        int armW = isSlim ? 3 : 4;
        double rightArmMinX = isSlim ? -7.0 : -8.0;
        double rightArmMaxX = -4.0;
        double leftArmMinX = 4.0;
        double leftArmMaxX = isSlim ? 7.0 : 8.0;

        // 1. Голова (8x8x8)
        AddDiagnosticCuboid(rootGroup, -4, 4, 8, 16, -4, 4,
            Color.FromRgb(255, 59, 48),    // Спереди: Ярко-красный
            Color.FromRgb(175, 82, 222),   // Сзади: Фиолетовый
            Color.FromRgb(255, 149, 0),    // Справа: Оранжевый
            Color.FromRgb(88, 86, 214),    // Слева: Индиго
            Color.FromRgb(255, 204, 0),    // Сверху: Желтый
            Color.FromRgb(52, 199, 89));   // Снизу: Зеленый

        // 2. Тело (8x12x4)
        AddDiagnosticCuboid(rootGroup, -4, 4, -4, 8, -2, 2,
            Color.FromRgb(0, 122, 255),    // Спереди: Синий
            Color.FromRgb(90, 200, 250),   // Сзади: Голубой
            Color.FromRgb(50, 173, 230),   // Справа: Морской
            Color.FromRgb(0, 199, 190),    // Слева: Бирюзовый
            Color.FromRgb(100, 210, 255),  // Сверху: Светло-голубой
            Color.FromRgb(10, 132, 255));  // Снизу: Глубокий синий

        // 3. Правая рука
        AddDiagnosticCuboid(rootGroup, rightArmMinX, rightArmMaxX, -4, 8, -2, 2,
            Color.FromRgb(48, 209, 88),    // Спереди: Салатовый
            Color.FromRgb(36, 138, 61),    // Сзади: Темно-зеленый
            Color.FromRgb(40, 205, 65),    // Справа: Изумрудный
            Color.FromRgb(30, 107, 48),    // Слева: Хвоя
            Color.FromRgb(133, 224, 163),  // Сверху: Мятный
            Color.FromRgb(20, 80, 35));    // Снизу: Болотный

        // 4. Левая рука
        AddDiagnosticCuboid(rootGroup, leftArmMinX, leftArmMaxX, -4, 8, -2, 2,
            Color.FromRgb(255, 100, 130),  // Спереди: Розовый
            Color.FromRgb(255, 55, 95),    // Сзади: Малиновый
            Color.FromRgb(224, 62, 82),    // Справа: Кармин
            Color.FromRgb(191, 45, 64),    // Слева: Бордовый
            Color.FromRgb(255, 168, 184),  // Сверху: Светло-розовый
            Color.FromRgb(142, 31, 47));   // Снизу: Вишневый

        // 5. Правая нога (4x12x4)
        AddDiagnosticCuboid(rootGroup, -4, 0, -16, -4, -2, 2,
            Color.FromRgb(255, 214, 10),   // Спереди: Золотой
            Color.FromRgb(255, 159, 10),   // Сзади: Охра
            Color.FromRgb(204, 122, 0),    // Справа: Бронзовый
            Color.FromRgb(230, 138, 0),    // Слева: Янтарный
            Color.FromRgb(255, 224, 102),  // Сверху: Светло-желтый
            Color.FromRgb(153, 92, 0));    // Снизу: Коричнево-золотой

        // 6. Левая нога (4x12x4)
        AddDiagnosticCuboid(rootGroup, 0, 4, -16, -4, -2, 2,
            Color.FromRgb(191, 90, 242),   // Спереди: Сиреневый
            Color.FromRgb(151, 71, 255),   // Сзади: Пурпурный
            Color.FromRgb(123, 44, 191),   // Справа: Фиалковый
            Color.FromRgb(96, 24, 148),    // Слева: Баклажанный
            Color.FromRgb(214, 162, 232),  // Сверху: Светло-лиловый
            Color.FromRgb(74, 18, 112));   // Снизу: Темно-фиолетовый

        return rootGroup;
    }

    private static void AddDiagnosticCuboid(
        Model3DGroup parent,
        double xMin, double xMax,
        double yMin, double yMax,
        double zMin, double zMax,
        Color cFront, Color cBack, Color cRight, Color cLeft, Color cTop, Color cBottom)
    {
        void AddSingleFace(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal, Color color)
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);

            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);

            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(1);

            mesh.TriangleIndices.Add(1);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(3);

            var mat = new DiffuseMaterial(new SolidColorBrush(color));
            mat.Freeze();
            var model = new GeometryModel3D(mesh, mat)
            {
                BackMaterial = mat
            };
            parent.Children.Add(model);
        }

        // 1. Спереди (+Z)
        AddSingleFace(new Point3D(xMin, yMax, zMax), new Point3D(xMax, yMax, zMax), new Point3D(xMin, yMin, zMax), new Point3D(xMax, yMin, zMax), new Vector3D(0, 0, 1), cFront);
        // 2. Сзади (-Z)
        AddSingleFace(new Point3D(xMax, yMax, zMin), new Point3D(xMin, yMax, zMin), new Point3D(xMax, yMin, zMin), new Point3D(xMin, yMin, zMin), new Vector3D(0, 0, -1), cBack);
        // 3. Справа (-X)
        AddSingleFace(new Point3D(xMin, yMax, zMin), new Point3D(xMin, yMax, zMax), new Point3D(xMin, yMin, zMin), new Point3D(xMin, yMin, zMax), new Vector3D(-1, 0, 0), cRight);
        // 4. Слева (+X)
        AddSingleFace(new Point3D(xMax, yMax, zMax), new Point3D(xMax, yMax, zMin), new Point3D(xMax, yMin, zMax), new Point3D(xMax, yMin, zMin), new Vector3D(1, 0, 0), cLeft);
        // 5. Сверху (+Y)
        AddSingleFace(new Point3D(xMin, yMax, zMin), new Point3D(xMax, yMax, zMin), new Point3D(xMin, yMax, zMax), new Point3D(xMax, yMax, zMax), new Vector3D(0, 1, 0), cTop);
        // 6. Снизу (-Y)
        AddSingleFace(new Point3D(xMin, yMin, zMax), new Point3D(xMax, yMin, zMax), new Point3D(xMin, yMin, zMin), new Point3D(xMax, yMin, zMin), new Vector3D(0, -1, 0), cBottom);
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
