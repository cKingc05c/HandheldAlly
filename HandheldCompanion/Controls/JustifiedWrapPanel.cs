using HandheldCompanion.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HandheldCompanion.Controls
{
    public sealed class JustifiedWrapPanel : VirtualizingPanel
    {
        private const double OverscanViewportMultiplier = 0.5d;
        private const double MinimumOverscan = 64.0d;

        public static readonly DependencyProperty TargetRowHeightProperty = DependencyProperty.Register(
            nameof(TargetRowHeight),
            typeof(double),
            typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(250.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemAspectRatioProperty = DependencyProperty.Register(
            nameof(ItemAspectRatio),
            typeof(double),
            typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(200.0 / 360.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemSpanProperty = DependencyProperty.RegisterAttached(
            "ItemSpan",
            typeof(int),
            typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static int GetItemSpan(UIElement element) => (int)element.GetValue(ItemSpanProperty);
        public static void SetItemSpan(UIElement element, int value) => element.SetValue(ItemSpanProperty, value);

        private LayoutInfo currentLayout = LayoutInfo.Empty;
        private ScrollViewer? observedScrollViewer;

        public JustifiedWrapPanel()
        {
            Loaded += JustifiedWrapPanel_Loaded;
            Unloaded += JustifiedWrapPanel_Unloaded;
        }

        public double TargetRowHeight
        {
            get => (double)GetValue(TargetRowHeightProperty);
            set => SetValue(TargetRowHeightProperty, value);
        }

        public double HorizontalSpacing
        {
            get => (double)GetValue(HorizontalSpacingProperty);
            set => SetValue(HorizontalSpacingProperty, value);
        }

        public double VerticalSpacing
        {
            get => (double)GetValue(VerticalSpacingProperty);
            set => SetValue(VerticalSpacingProperty, value);
        }

        public double ItemAspectRatio
        {
            get => (double)GetValue(ItemAspectRatioProperty);
            set => SetValue(ItemAspectRatioProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            AttachScrollViewer();

            ItemsControl? itemsOwner = ItemsControl.GetItemsOwner(this);
            if (itemsOwner is null || itemsOwner.Items.Count == 0)
            {
                currentLayout = LayoutInfo.Empty;
                CleanupItems(-1, -1, itemsOwner);
                return new Size(0.0, 0.0);
            }

            double availableWidth = ResolveAvailableWidth(availableSize);
            currentLayout = BuildLayout(itemsOwner, availableWidth);

            bool isVirtualizing = GetIsVirtualizing(itemsOwner);
            (int firstIndex, int lastIndex) = GetRealizationRange(currentLayout, isVirtualizing);

            CleanupItems(firstIndex, lastIndex, itemsOwner);

            if (firstIndex >= 0)
                RealizeItems(itemsOwner, firstIndex, lastIndex);

            return CoerceSize(currentLayout.DesiredWidth, currentLayout.DesiredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
            {
                UIElement child = InternalChildren[childIndex];
                int itemIndex = GetItemIndexFromContainer(child);

                if (itemIndex < 0 || !currentLayout.TryGetItemLayout(itemIndex, out ItemLayout? itemLayout))
                {
                    child.Arrange(Rect.Empty);
                    continue;
                }

                child.Arrange(CoerceRect(itemLayout.Bounds));
            }

            double finalWidth = Math.Max(CoerceNonNegativeFinite(finalSize.Width), CoerceNonNegativeFinite(currentLayout.DesiredWidth));
            return CoerceSize(finalWidth, currentLayout.DesiredHeight);
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            currentLayout = LayoutInfo.Empty;
            InvalidateMeasure();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (sizeInfo.WidthChanged)
                InvalidateMeasure();
        }

        private void JustifiedWrapPanel_Loaded(object sender, RoutedEventArgs e)
        {
            AttachScrollViewer();
        }

        private void JustifiedWrapPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachScrollViewer();
        }

        private void AttachScrollViewer()
        {
            ScrollViewer? nextScrollViewer = WPFUtils.FindParent<ScrollViewer>(this);
            if (ReferenceEquals(observedScrollViewer, nextScrollViewer))
                return;

            DetachScrollViewer();
            observedScrollViewer = nextScrollViewer;

            if (observedScrollViewer is null)
                return;

            observedScrollViewer.ScrollChanged += ObservedScrollViewer_ScrollChanged;
            observedScrollViewer.SizeChanged += ObservedScrollViewer_SizeChanged;
        }

        private void DetachScrollViewer()
        {
            if (observedScrollViewer is null)
                return;

            observedScrollViewer.ScrollChanged -= ObservedScrollViewer_ScrollChanged;
            observedScrollViewer.SizeChanged -= ObservedScrollViewer_SizeChanged;
            observedScrollViewer = null;
        }

        private void ObservedScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.HorizontalChange != 0 || e.ViewportWidthChange != 0)
                InvalidateMeasure();
        }

        private void ObservedScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged || e.HeightChanged)
                InvalidateMeasure();
        }

        private double ResolveAvailableWidth(Size availableSize)
        {
            if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0.0)
                return availableSize.Width;

            if (observedScrollViewer is not null)
            {
                double scrollWidth = observedScrollViewer.ViewportWidth > 0.0 ? observedScrollViewer.ViewportWidth : observedScrollViewer.ActualWidth;
                if (scrollWidth > 0.0)
                    return scrollWidth;
            }

            return ActualWidth > 0.0 ? ActualWidth : 0.0;
        }

        private static double CoerceNonNegativeFinite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 ? 0.0 : value;
        }

        private static double CoerceFiniteCoordinate(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }

        private static Size CoerceSize(double width, double height)
        {
            return new Size(CoerceNonNegativeFinite(width), CoerceNonNegativeFinite(height));
        }

        private static Rect CoerceRect(Rect bounds)
        {
            double width = CoerceNonNegativeFinite(bounds.Width);
            double height = CoerceNonNegativeFinite(bounds.Height);
            if (width <= 0.0 || height <= 0.0)
                return Rect.Empty;

            return new Rect(CoerceFiniteCoordinate(bounds.X), CoerceFiniteCoordinate(bounds.Y), width, height);
        }

        private LayoutInfo BuildLayout(ItemsControl itemsOwner, double availableWidth)
        {
            int itemCount = itemsOwner.Items.Count;
            if (itemCount == 0)
                return LayoutInfo.Empty;

            bool hasFiniteWidth = !double.IsInfinity(availableWidth) && availableWidth > 0.0;
            double targetHeight = Math.Max(1.0, TargetRowHeight);
            double aspectRatio = Math.Max(0.01, ItemAspectRatio);
            double targetWidth = targetHeight * aspectRatio;

            List<RowLayout> rows = new();
            Dictionary<int, ItemLayout> itemLayouts = new(itemCount);
            List<PendingItemLayout> pendingItems = new();
            double pendingItemsWidth = 0.0;
            double y = 0.0;

            for (int index = 0; index < itemCount; index++)
            {
                int itemSpan = Math.Max(1, ReadItemSpan(itemsOwner, index));
                double width = (targetWidth * itemSpan) + (HorizontalSpacing * (itemSpan - 1));

                pendingItems.Add(new PendingItemLayout(index, width));
                pendingItemsWidth += width;

                double currentRowWidth = pendingItemsWidth + (Math.Max(0, pendingItems.Count - 1) * HorizontalSpacing);
                if (hasFiniteWidth && currentRowWidth >= availableWidth)
                {
                    RowLayout row = CreateRowLayout(pendingItems, availableWidth, targetHeight, justify: true, y, rows.Count > 0 ? rows[^1] : null);
                    rows.Add(row);

                    foreach (ItemLayout itemLayout in row.Items)
                        itemLayouts[itemLayout.Index] = itemLayout;

                    y += row.Height + VerticalSpacing;
                    pendingItems = new List<PendingItemLayout>();
                    pendingItemsWidth = 0.0;
                }
            }

            if (pendingItems.Count > 0)
            {
                RowLayout row = CreateRowLayout(pendingItems, availableWidth, targetHeight, justify: false, y, rows.Count > 0 ? rows[^1] : null);
                rows.Add(row);

                foreach (ItemLayout itemLayout in row.Items)
                    itemLayouts[itemLayout.Index] = itemLayout;

                y += row.Height;
            }
            else if (rows.Count > 0)
            {
                y -= VerticalSpacing;
            }

            double desiredWidth = hasFiniteWidth ? availableWidth : GetDesiredWidth(rows);
            return new LayoutInfo(desiredWidth, Math.Max(0.0, y), rows, itemLayouts, itemCount);
        }

        private RowLayout CreateRowLayout(IReadOnlyList<PendingItemLayout> pendingItems, double availableWidth, double targetHeight, bool justify, double y, RowLayout? previousRow)
        {
            double totalItemWidth = 0.0;
            foreach (PendingItemLayout item in pendingItems)
                totalItemWidth += item.Width;

            int gapCount = Math.Max(0, pendingItems.Count - 1);
            double scale = 1.0;

            if (!double.IsInfinity(availableWidth) && availableWidth > 0.0)
            {
                double availableItemWidth = Math.Max(1.0, availableWidth - (gapCount * HorizontalSpacing));
                if (justify || totalItemWidth > availableItemWidth)
                    scale = Math.Max(0.01, availableItemWidth / totalItemWidth);
            }

            if (!justify && previousRow is not null)
            {
                double previousRowScale = previousRow.Height / Math.Max(1.0, targetHeight);
                scale = Math.Min(scale, previousRowScale);
            }

            double rowHeight = Math.Max(1.0, targetHeight * scale);
            RowLayout row = new(y, rowHeight);
            double x = 0.0;

            foreach (PendingItemLayout item in pendingItems)
            {
                double itemWidth = Math.Max(1.0, item.Width * scale);
                row.Items.Add(new ItemLayout(item.Index, new Rect(x, y, itemWidth, rowHeight)));
                x += itemWidth + HorizontalSpacing;
            }

            row.Width = pendingItems.Count > 0 ? Math.Max(0.0, x - HorizontalSpacing) : 0.0;
            return row;
        }

        private int ReadItemSpan(ItemsControl itemsOwner, int index)
        {
            if (itemsOwner.ItemContainerGenerator.ContainerFromIndex(index) is UIElement container)
            {
                int attachedSpan = GetItemSpan(container);
                if (attachedSpan > 1)
                    return attachedSpan;

                if (container is FrameworkElement frameworkElement)
                {
                    int tagSpan = ReadSpanFromTag(frameworkElement.Tag);
                    if (tagSpan > 1)
                        return tagSpan;

                    if (TryGetBooleanProperty(frameworkElement.DataContext, "IsLiked", out bool isLiked) && isLiked)
                        return 3;
                }
            }

            if (TryGetBooleanProperty(itemsOwner.DataContext, "IsWideView", out bool isWideView) && isWideView)
                return 3;

            object item = itemsOwner.Items[index];

            if (TryGetIntProperty(item, "ItemSpan", out int reflectedSpan) && reflectedSpan > 0)
                return reflectedSpan;

            if (TryGetBooleanProperty(item, "IsLiked", out bool isFavorite) && isFavorite)
                return 3;

            return 1;
        }

        private static int ReadSpanFromTag(object? tag)
        {
            if (tag is int tagInt && tagInt > 0)
                return tagInt;

            if (tag is string tagString)
            {
                if (int.TryParse(tagString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int invariantSpan) && invariantSpan > 0)
                    return invariantSpan;

                if (int.TryParse(tagString, NumberStyles.Integer, CultureInfo.CurrentCulture, out int currentSpan) && currentSpan > 0)
                    return currentSpan;
            }

            if (tag is double tagDouble && tagDouble >= 1.0)
                return Math.Max(1, (int)Math.Round(tagDouble));

            return 0;
        }

        private static bool TryGetIntProperty(object? instance, string propertyName, out int value)
        {
            value = 0;

            if (instance is null)
                return false;

            PropertyInfo? propertyInfo = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (propertyInfo is null)
                return false;

            object? propertyValue = propertyInfo.GetValue(instance);
            if (propertyValue is int intValue && intValue > 0)
            {
                value = intValue;
                return true;
            }

            if (propertyValue is double doubleValue && doubleValue >= 1.0)
            {
                value = Math.Max(1, (int)Math.Round(doubleValue));
                return true;
            }

            return false;
        }

        private static bool TryGetBooleanProperty(object? instance, string propertyName, out bool value)
        {
            value = false;

            if (instance is null)
                return false;

            PropertyInfo? propertyInfo = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (propertyInfo?.PropertyType != typeof(bool))
                return false;

            object? propertyValue = propertyInfo.GetValue(instance);
            if (propertyValue is not bool booleanValue)
                return false;

            value = booleanValue;
            return true;
        }

        private (int FirstIndex, int LastIndex) GetRealizationRange(LayoutInfo layout, bool isVirtualizing)
        {
            if (layout.Rows.Count == 0)
                return (-1, -1);

            if (!isVirtualizing || !TryGetViewportRange(layout.DesiredHeight, out double viewportTop, out double viewportBottom))
                return (0, layout.ItemCount - 1);

            int firstIndex = -1;
            int lastIndex = -1;

            foreach (RowLayout row in layout.Rows)
            {
                if (row.Bottom <= viewportTop || row.Top >= viewportBottom)
                    continue;

                firstIndex = firstIndex < 0 ? row.FirstIndex : Math.Min(firstIndex, row.FirstIndex);
                lastIndex = Math.Max(lastIndex, row.LastIndex);
            }

            if (firstIndex < 0 || lastIndex < 0)
                return GetNearestRealizationRange(layout, viewportTop, viewportBottom);

            return (firstIndex, lastIndex);
        }

        private static (int FirstIndex, int LastIndex) GetNearestRealizationRange(LayoutInfo layout, double viewportTop, double viewportBottom)
        {
            RowLayout nearestRow = layout.Rows[0];
            double nearestDistance = GetViewportDistance(nearestRow, viewportTop, viewportBottom);

            for (int i = 1; i < layout.Rows.Count; i++)
            {
                RowLayout candidate = layout.Rows[i];
                double candidateDistance = GetViewportDistance(candidate, viewportTop, viewportBottom);
                if (candidateDistance >= nearestDistance)
                    continue;

                nearestRow = candidate;
                nearestDistance = candidateDistance;
            }

            return (nearestRow.FirstIndex, nearestRow.LastIndex);
        }

        private static double GetViewportDistance(RowLayout row, double viewportTop, double viewportBottom)
        {
            if (row.Bottom <= viewportTop)
                return viewportTop - row.Bottom;

            if (row.Top >= viewportBottom)
                return row.Top - viewportBottom;

            return 0.0;
        }

        private bool TryGetViewportRange(double extentHeight, out double viewportTop, out double viewportBottom)
        {
            viewportTop = 0.0;
            viewportBottom = 0.0;

            if (observedScrollViewer is null)
                return false;

            double viewportHeight = observedScrollViewer.ViewportHeight > 0.0 ? observedScrollViewer.ViewportHeight : observedScrollViewer.ActualHeight;
            if (viewportHeight <= 0.0 || double.IsInfinity(viewportHeight))
                return false;

            try
            {
                Point origin = TranslatePoint(new Point(0, 0), observedScrollViewer);
                double rawViewportTop = -origin.Y;
                double rawViewportBottom = rawViewportTop + viewportHeight;

                if (rawViewportBottom <= 0.0 || rawViewportTop >= extentHeight)
                    return false;

                double overscan = 0.0d; // Math.Max(TargetRowHeight, viewportHeight * OverscanViewportMultiplier);
                viewportTop = Math.Max(0.0, rawViewportTop - overscan);
                viewportBottom = Math.Min(extentHeight, rawViewportBottom + overscan);
                return viewportBottom > viewportTop;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void CleanupItems(int firstIndex, int lastIndex, ItemsControl? itemsOwner)
        {
            bool useRecycling = itemsOwner is not null && GetVirtualizationMode(itemsOwner) == VirtualizationMode.Recycling;
            IRecyclingItemContainerGenerator? recyclingGenerator = useRecycling ? ItemContainerGenerator as IRecyclingItemContainerGenerator : null;

            for (int childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
            {
                UIElement child = InternalChildren[childIndex];
                int itemIndex = itemsOwner?.ItemContainerGenerator.IndexFromContainer(child) ?? GetItemIndexFromContainer(child);

                if (itemIndex >= firstIndex && itemIndex <= lastIndex)
                    continue;

                if (itemIndex < 0)
                {
                    RemoveInternalChildRange(childIndex, 1);
                    continue;
                }

                GeneratorPosition generatorPosition = new(childIndex, 0);

                if (recyclingGenerator is not null)
                    recyclingGenerator.Recycle(generatorPosition, 1);
                else
                    ItemContainerGenerator.Remove(generatorPosition, 1);

                RemoveInternalChildRange(childIndex, 1);
            }
        }

        private void RealizeItems(ItemsControl itemsOwner, int firstIndex, int lastIndex)
        {
            GeneratorPosition startPosition = ItemContainerGenerator.GeneratorPositionFromIndex(firstIndex);
            int childIndex = GetChildIndexFromGeneratorPosition(startPosition);

            using (ItemContainerGenerator.StartAt(startPosition, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
                {
                    bool newlyRealized;
                    DependencyObject child = ItemContainerGenerator.GenerateNext(out newlyRealized);
                    UIElement element = (UIElement)child;

                    if (newlyRealized)
                    {
                        if (childIndex >= InternalChildren.Count)
                            AddInternalChild(element);
                        else
                            InsertInternalChild(childIndex, element);

                        ItemContainerGenerator.PrepareItemContainer(child);
                    }
                    else
                    {
                        int currentIndex = InternalChildren.IndexOf(element);
                        if (currentIndex != childIndex)
                        {
                            if (currentIndex >= 0)
                                RemoveInternalChildRange(currentIndex, 1);

                            if (childIndex >= InternalChildren.Count)
                                AddInternalChild(element);
                            else
                                InsertInternalChild(childIndex, element);
                        }
                    }

                    if (currentLayout.TryGetItemLayout(itemIndex, out ItemLayout? itemLayout))
                        element.Measure(CoerceSize(itemLayout.Bounds.Width, itemLayout.Bounds.Height));
                }
            }
        }

        private static int GetChildIndexFromGeneratorPosition(GeneratorPosition generatorPosition)
        {
            if (generatorPosition.Index < 0)
                return 0;

            return generatorPosition.Index + (generatorPosition.Offset == 0 ? 0 : 1);
        }

        private int GetItemIndexFromContainer(UIElement child)
        {
            ItemsControl? itemsOwner = ItemsControl.GetItemsOwner(this);
            return itemsOwner?.ItemContainerGenerator.IndexFromContainer(child) ?? -1;
        }

        private static double GetDesiredWidth(IReadOnlyList<RowLayout> rows)
        {
            double desiredWidth = 0.0;

            foreach (RowLayout row in rows)
                desiredWidth = Math.Max(desiredWidth, row.Width);

            return desiredWidth;
        }

        private sealed class LayoutInfo
        {
            public static readonly LayoutInfo Empty = new LayoutInfo(0.0, 0.0, Array.Empty<RowLayout>(), new Dictionary<int, ItemLayout>(), 0);

            public LayoutInfo(double desiredWidth, double desiredHeight, IReadOnlyList<RowLayout> rows, IReadOnlyDictionary<int, ItemLayout> itemLayouts, int itemCount)
            {
                DesiredWidth = desiredWidth;
                DesiredHeight = desiredHeight;
                Rows = rows;
                ItemLayouts = itemLayouts;
                ItemCount = itemCount;
            }

            public double DesiredWidth { get; }

            public double DesiredHeight { get; }

            public IReadOnlyList<RowLayout> Rows { get; }

            public IReadOnlyDictionary<int, ItemLayout> ItemLayouts { get; }

            public int ItemCount { get; }

            public bool TryGetItemLayout(int index, out ItemLayout? itemLayout)
            {
                if (ItemLayouts.TryGetValue(index, out ItemLayout? value))
                {
                    itemLayout = value;
                    return true;
                }

                itemLayout = null;
                return false;
            }
        }

        private sealed class RowLayout
        {
            public RowLayout(double top, double height)
            {
                Top = top;
                Height = height;
                Items = new List<ItemLayout>();
            }

            public double Top { get; }

            public double Bottom => Top + Height;

            public double Height { get; }

            public double Width { get; set; }

            public int FirstIndex => Items.Count > 0 ? Items[0].Index : -1;

            public int LastIndex => Items.Count > 0 ? Items[^1].Index : -1;

            public List<ItemLayout> Items { get; }
        }

        private sealed class ItemLayout
        {
            public ItemLayout(int index, Rect bounds)
            {
                Index = index;
                Bounds = bounds;
            }

            public int Index { get; }

            public Rect Bounds { get; }
        }

        private sealed class PendingItemLayout
        {
            public PendingItemLayout(int index, double width)
            {
                Index = index;
                Width = width;
            }

            public int Index { get; }

            public double Width { get; }
        }
    }
}
