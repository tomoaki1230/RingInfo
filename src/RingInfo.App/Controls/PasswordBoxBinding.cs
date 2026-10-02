using System.Windows;
using System.Windows.Controls;

namespace RingInfo.App.Controls;

/// <summary>PasswordBox.Password を ViewModel とバインドするための添付プロパティ</summary>
public static class PasswordBoxBinding
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxBinding),
        // 既定値を null にして、空文字のバインドでも変更通知（＝イベント登録）が必ず走るようにする
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxBinding));

    public static string? GetPassword(DependencyObject element) => (string?)element.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject element, string value) => element.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= OnPasswordBoxChanged;
        if (!(bool)box.GetValue(IsUpdatingProperty))
        {
            box.Password = e.NewValue as string ?? "";
        }

        box.PasswordChanged += OnPasswordBoxChanged;
    }

    private static void OnPasswordBoxChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
