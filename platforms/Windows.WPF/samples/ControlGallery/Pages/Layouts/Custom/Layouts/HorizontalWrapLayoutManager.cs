using Microsoft.Maui.Layouts;
using System.Diagnostics;
using StackLayoutManager = Microsoft.Maui.Layouts.StackLayoutManager;

namespace CustomLayouts
{
    public class HorizontalWrapLayoutManager : StackLayoutManager
    {
        HorizontalWrapLayout _layout;

        public HorizontalWrapLayoutManager(HorizontalWrapLayout horizontalWrapLayout) : base(horizontalWrapLayout)
        {
            _layout = horizontalWrapLayout;
        }

       
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var padding = _layout.Padding;

            widthConstraint -= padding.HorizontalThickness;

            double currentRowWidth = 0;
            double currentRowHeight = 0;
            double totalWidth = 0;
            double totalHeight = 0;

            for (int n = 0; n < _layout.Count; n++)
            {
                var child = _layout[n];

                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var measure = child.Measure(double.PositiveInfinity, heightConstraint);

                if(n == 0){ // first item, go ahead and set a row height to start
                    currentRowHeight = Math.Max(currentRowHeight, measure.Height);
                    totalHeight += currentRowHeight;
                    Debug.WriteLine($"item{n} totalHeight: {totalHeight}");
                    // totalHeight += _layout.Spacing;
                }

                // Will adding this IView put us past the edge?
                if (currentRowWidth + measure.Width > widthConstraint)
                {
                    // new row
                    currentRowHeight = Math.Max(currentRowHeight, measure.Height);
                    
                    // Keep track of the width so far
                    totalWidth = Math.Max(totalWidth, currentRowWidth);
                    
                    totalHeight += currentRowHeight;
                    totalHeight += _layout.Spacing;
                    // Debug.WriteLine($"item{n} totalHeight: {totalHeight}");
                    // if (n < _layout.Count - 1) 
                    // {
                    //     totalHeight += _layout.Spacing;
                    //     // Debug.WriteLine($"item{n} totalHeight + spacing: {totalHeight}");
                    // }
                    
                    currentRowWidth = measure.Width;
                    if (n < _layout.Count - 1) 
                    {
                        currentRowWidth += _layout.Spacing;
                    }
                    // currentRowHeight = measure.Height;
                }
                else
                {
                    // add to row
                    currentRowWidth += measure.Width;                    
                    
                    if (n < _layout.Count - 1) 
                    {
                        currentRowWidth += _layout.Spacing;
                    }
                }
            }

            // Account for the last row
            // totalWidth = Math.Max(totalWidth, currentRowWidth);
            // totalHeight += currentRowHeight;

            // Account for padding
            totalWidth += padding.HorizontalThickness;
            totalHeight += padding.VerticalThickness;

            var finalHeight = ResolveConstraints(heightConstraint, Stack.Height, totalHeight, Stack.MinimumHeight, Stack.MaximumHeight);
            var finalWidth = ResolveConstraints(widthConstraint, Stack.Width, totalWidth, Stack.MinimumWidth, Stack.MaximumWidth);

            return new Size(finalWidth, finalHeight);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var padding = Stack.Padding;
            double top = padding.Top + bounds.Top;
            double left = padding.Left + bounds.Left;

            double currentRowTop = top;
            double currentX = left;
            double currentRowHeight = 0;

            double maxStackWidth = currentX;

            for (int n = 0; n < _layout.Count; n++)
            {
                var child = _layout[n];

                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                if (currentX + child.DesiredSize.Width > bounds.Right) 
                {
                    // Keep track of our maximum width so far
                    maxStackWidth = Math.Max(maxStackWidth, currentX);

                    // Move down to the next row
                    currentX = left;
                    currentRowTop += currentRowHeight + _layout.Spacing;
                    currentRowHeight = 0;
                }

                var destination = new Rect(currentX, currentRowTop, child.DesiredSize.Width, child.DesiredSize.Height);
                child.Arrange(destination);

                currentX += destination.Width + _layout.Spacing;
                currentRowHeight = Math.Max(currentRowHeight, destination.Height);
            }

            var actual = new Size(maxStackWidth, currentRowTop + currentRowHeight);

            return actual.AdjustForFill(bounds, Stack);
        }

    }
}
