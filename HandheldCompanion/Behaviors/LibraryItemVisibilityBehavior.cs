using HandheldCompanion.ViewModels;
using System;
using System.Windows;

namespace HandheldCompanion.Behaviors;

public static class LibraryItemVisibilityBehavior
{
    public static readonly DependencyProperty TrackVisibilityProperty = DependencyProperty.RegisterAttached(
        "TrackVisibility",
        typeof(bool),
        typeof(LibraryItemVisibilityBehavior),
        new PropertyMetadata(false, OnTrackVisibilityChanged));

    private static readonly DependencyProperty RegistrationProperty = DependencyProperty.RegisterAttached(
        "Registration",
        typeof(Registration),
        typeof(LibraryItemVisibilityBehavior),
        new PropertyMetadata(null));

    public static bool GetTrackVisibility(DependencyObject element)
    {
        return (bool)element.GetValue(TrackVisibilityProperty);
    }

    public static void SetTrackVisibility(DependencyObject element, bool value)
    {
        element.SetValue(TrackVisibilityProperty, value);
    }

    private static void OnTrackVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        GetRegistration(element)?.Dispose();
        element.ClearValue(RegistrationProperty);

        if ((bool)e.NewValue)
            element.SetValue(RegistrationProperty, new Registration(element));
    }

    private static Registration? GetRegistration(DependencyObject dependencyObject)
    {
        return dependencyObject.GetValue(RegistrationProperty) as Registration;
    }

    private static void SetVisualVisibility(object? dataContext, bool isVisible, bool immediate = false)
    {
        if (dataContext is ProfileViewModel profileViewModel)
            profileViewModel.SetVisualsVisible(isVisible, immediate);
    }

    private sealed class Registration : IDisposable
    {
        private readonly FrameworkElement element;
        private bool disposed;

        public Registration(FrameworkElement element)
        {
            this.element = element;
            element.Loaded += Element_Loaded;
            element.Unloaded += Element_Unloaded;
            element.DataContextChanged += Element_DataContextChanged;
            element.IsVisibleChanged += Element_IsVisibleChanged;

            UpdateCurrentVisibility(immediateWhenHidden: true);
        }

        private void Element_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateCurrentVisibility();
        }

        private void Element_Unloaded(object sender, RoutedEventArgs e)
        {
            SetVisualVisibility(element.DataContext, false, immediate: true);
        }

        private void Element_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SetVisualVisibility(e.OldValue, false, immediate: true);
            UpdateCurrentVisibility(immediateWhenHidden: true);
        }

        private void Element_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            bool isVisible = (bool)e.NewValue && element.IsLoaded;
            SetVisualVisibility(element.DataContext, isVisible, immediate: !isVisible);
        }

        private void UpdateCurrentVisibility(bool immediateWhenHidden = false)
        {
            bool isVisible = element.IsLoaded && element.IsVisible;
            SetVisualVisibility(element.DataContext, isVisible, immediate: immediateWhenHidden && !isVisible);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            element.Loaded -= Element_Loaded;
            element.Unloaded -= Element_Unloaded;
            element.DataContextChanged -= Element_DataContextChanged;
            element.IsVisibleChanged -= Element_IsVisibleChanged;
            SetVisualVisibility(element.DataContext, false, immediate: true);
        }
    }
}
