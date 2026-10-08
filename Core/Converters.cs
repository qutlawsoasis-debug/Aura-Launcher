using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AuraLauncher.Core;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            return v != Visibility.Visible;
        }
        return false;
    }
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InverseIntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToEmberOrDarkBgConverter : IValueConverter
{
    private static readonly SolidColorBrush EmberBrush = CreateFrozenBrush("#33F2A63C");
    private static readonly SolidColorBrush DarkBrush = CreateFrozenBrush("#141E26");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? EmberBrush : DarkBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public class BoolToEmberOrLineBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush EmberBrush = CreateFrozenBrush("#F2A63C");
    private static readonly SolidColorBrush LineBrush = CreateFrozenBrush("#24EAF1EF");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? EmberBrush : LineBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public class BoolToInkOrDimBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InkBrush = CreateFrozenBrush("#EAF1EF");
    private static readonly SolidColorBrush DimBrush = CreateFrozenBrush("#9EEAF1EF");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? InkBrush : DimBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public class BoolToInkOrMuteBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InkBrush = CreateFrozenBrush("#EAF1EF");
    private static readonly SolidColorBrush MuteBrush = CreateFrozenBrush("#8FA7A4");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? InkBrush : MuteBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public class BoolToEnabledTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "ВКЛ" : "ВЫКЛ";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
