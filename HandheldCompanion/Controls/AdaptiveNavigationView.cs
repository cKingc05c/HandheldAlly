using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HandheldCompanion.Controls
{
    /// <summary>
    /// Custom NavigationView that switches items to icon-only mode when width is insufficient,
    /// instead of showing an overflow menu.
    /// </summary>
    public class AdaptiveNavigationView : NavigationView
    {
        private ItemsRepeater m_topNavRepeater;
        private Button m_topNavOverflowButton;
        private bool m_isIconOnlyMode = false;
        private double m_lastMeasuredWidth = 0;

        private const double RESERVED_SPACE = 200.0;            // Space for padding, footer, etc.
        private const double HYSTERESIS_MARGIN = 20.0;          // Extra margin to prevent rapid switching (reduced from 50)

        public AdaptiveNavigationView()
        {
            this.Loaded += OnLoaded;
            this.LayoutUpdated += OnLayoutUpdated;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // Get template children
            m_topNavRepeater = GetTemplateChild("TopNavMenuItemsHost") as ItemsRepeater;
            m_topNavOverflowButton = GetTemplateChild("TopNavOverflowButton") as Button;

            // Force overflow button to always be hidden
            if (m_topNavOverflowButton != null)
            {
                m_topNavOverflowButton.Visibility = Visibility.Collapsed;
                m_topNavOverflowButton.IsEnabled = false;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Let base class do its measurement
            Size baseSize = base.MeasureOverride(availableSize);

            // Only process if we're in top pane mode
            if (PaneDisplayMode == NavigationViewPaneDisplayMode.Top && !double.IsInfinity(availableSize.Width))
            {
                double currentWidth = availableSize.Width;

                // Only update if width actually changed significantly
                if (Math.Abs(currentWidth - m_lastMeasuredWidth) > 1)
                {
                    m_lastMeasuredWidth = currentWidth;
                    Dispatcher.BeginInvoke(new Action(() => UpdateItemDisplayMode(currentWidth)),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }

            return baseSize;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Initial update after load
            if (ActualWidth > 0)
            {
                UpdateItemDisplayMode(ActualWidth);
            }
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            // Ensure overflow button stays hidden
            if (m_topNavOverflowButton != null && m_topNavOverflowButton.Visibility != Visibility.Collapsed)
            {
                m_topNavOverflowButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateItemDisplayMode(double availableWidth)
        {
            if (m_topNavRepeater == null || PaneDisplayMode != NavigationViewPaneDisplayMode.Top)
                return;

            try
            {
                var items = GetTopNavigationViewItems();
                if (items.Count == 0)
                    return;

                double widthIfIconText = MeasureRequiredWidth(items, iconOnly: false);
                double widthIfIconOnly = MeasureRequiredWidth(items, iconOnly: true);

                ApplyDisplayModeToItems(items, m_isIconOnlyMode, useTransitions: false);

                double availableForItems = availableWidth - RESERVED_SPACE;

                bool shouldBeIconOnly;

                if (m_isIconOnlyMode)
                {
                    // Currently IconOnly: switch to IconText only if we have PLENTY of space
                    // Add hysteresis margin to prevent immediate switch back
                    shouldBeIconOnly = availableForItems < (widthIfIconText + HYSTERESIS_MARGIN);
                }
                else
                {
                    // Currently IconText: switch to IconOnly only if we really need to
                    // Subtract hysteresis margin to prevent immediate switch back
                    shouldBeIconOnly = availableForItems < (widthIfIconText - HYSTERESIS_MARGIN);
                }

                // Only apply changes if mode actually changed
                if (shouldBeIconOnly != m_isIconOnlyMode)
                {
                    m_isIconOnlyMode = shouldBeIconOnly;
                    ApplyDisplayModeToItems(items, shouldBeIconOnly);
                }
            }
            catch
            { }
        }

        private List<NavigationViewItem> GetTopNavigationViewItems()
        {
            var items = new List<NavigationViewItem>();

            if (MenuItems == null)
                return items;

            // Get items directly from MenuItems collection
            foreach (var menuItem in MenuItems)
            {
                if (menuItem is NavigationViewItem navItem && navItem.Visibility == Visibility.Visible)
                {
                    items.Add(navItem);
                }
            }

            return items;
        }

        private NavigationViewItemPresenter? FindNavigationViewItemPresenter(NavigationViewItem item)
        {
            if (item == null)
                return null;

            return FindVisualChild<NavigationViewItemPresenter>(item);
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);

                    if (child is T result)
                        return result;

                    var childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            catch
            {
                // Visual tree not ready
            }

            return null;
        }

        private void ApplyDisplayModeToItems(List<NavigationViewItem> items, bool iconOnly)
        {
            ApplyDisplayModeToItems(items, iconOnly, useTransitions: true);
        }

        private double MeasureRequiredWidth(List<NavigationViewItem> items, bool iconOnly)
        {
            ApplyDisplayModeToItems(items, iconOnly, useTransitions: false);

            double totalWidth = 0.0;
            Size measureSize = new Size(double.PositiveInfinity, double.PositiveInfinity);

            foreach (NavigationViewItem item in items)
            {
                try
                {
                    item.InvalidateMeasure();
                    item.UpdateLayout();
                    item.Measure(measureSize);

                    double itemWidth = item.DesiredSize.Width;
                    if (double.IsNaN(itemWidth) || itemWidth <= 0)
                        itemWidth = item.ActualWidth;

                    totalWidth += itemWidth;
                }
                catch
                { }
            }

            return totalWidth;
        }

        private void ApplyDisplayModeToItems(List<NavigationViewItem> items, bool iconOnly, bool useTransitions)
        {
            foreach (var item in items)
            {
                try
                {
                    var presenter = FindNavigationViewItemPresenter(item);
                    if (presenter != null)
                    {
                        // Find the NavigationViewIconPositionStatesListener in the presenter's template
                        var listener = FindVisualStateGroupListener(presenter, "NavigationViewIconPositionStatesListener");

                        if (listener != null)
                        {
                            // Set the CurrentStateName property directly via reflection
                            string stateName = iconOnly ? "IconOnly" : "IconOnLeft";
                            SetListenerState(listener, stateName);
                        }
                        else
                        {
                            // Fallback to standard visual state manager
                            string stateName = iconOnly ? "IconOnly" : "IconOnLeft";
                            bool success = VisualStateManager.GoToState(presenter, stateName, useTransitions);
                        }
                    }
                }
                catch
                { }
            }
        }

        private object? FindVisualStateGroupListener(DependencyObject element, string listenerName)
        {
            if (element == null)
                return null;

            try
            {
                // Try to find the listener by name in the template
                if (element is FrameworkElement fe)
                {
                    var listener = fe.FindName(listenerName);
                    if (listener != null)
                        return listener;
                }

                // Search children
                int childCount = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(element, i);
                    var found = FindVisualStateGroupListener(child, listenerName);
                    if (found != null)
                        return found;
                }
            }
            catch
            { }

            return null;
        }

        private void SetListenerState(object listener, string stateName)
        {
            try
            {
                // Use reflection to set the CurrentStateName property
                var listenerType = listener.GetType();
                var currentStateProperty = listenerType.GetProperty("CurrentStateName");

                if (currentStateProperty != null && currentStateProperty.CanWrite)
                {
                    currentStateProperty.SetValue(listener, stateName);
                }
            }
            catch
            { }
        }
    }
}
