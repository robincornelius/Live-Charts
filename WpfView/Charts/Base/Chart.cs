﻿//The MIT License(MIT)

//copyright(c) 2016 Alberto Rodriguez

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LiveCharts.Charts;
using LiveCharts.Defaults;
using LiveCharts.Definitions.Charts;
using LiveCharts.Definitions.Series;
using LiveCharts.Dtos;
using LiveCharts.Events;
using LiveCharts.Helpers;
using LiveCharts.Wpf.Components;
using LiveCharts.Wpf.Points;

namespace LiveCharts.Wpf.Charts.Base
{
    /// <summary>
    /// Base chart class
    /// </summary>
    public abstract class Chart : UserControl, IChartView
    {
        /// <summary>
        /// Chart core model, the model calculates the chart.
        /// </summary>
        protected ChartCore ChartCoreModel;
        
        #region Constructors

        /// <summary>
        /// Initializes a new instance of Chart class
        /// </summary>
        protected Chart()
        {
            Canvas = new Canvas();
            Content = Canvas;

            DrawMargin = new Canvas {ClipToBounds = true};
            Canvas.Children.Add(DrawMargin);

            TooltipTimeoutTimer = new DispatcherTimer();

            SetCurrentValue(MinHeightProperty, 50d);
            SetCurrentValue(MinWidthProperty, 80d);

            SetCurrentValue(AnimationsSpeedProperty, TimeSpan.FromMilliseconds(300));
            SetCurrentValue(TooltipTimeoutProperty, TimeSpan.FromMilliseconds(800));

            SetCurrentValue(AxisXProperty, new AxesCollection());
            SetCurrentValue(AxisYProperty, new AxesCollection());

            SetCurrentValue(ChartLegendProperty, new DefaultLegend());
            SetCurrentValue(DataTooltipProperty, new DefaultTooltip());

            Colors = new List<Color>
            {
                Color.FromRgb(33, 149, 242),
                Color.FromRgb(243, 67, 54),
                Color.FromRgb(254, 192, 7),
                Color.FromRgb(96, 125, 138),
                Color.FromRgb(0, 187, 211),
                Color.FromRgb(232, 30, 99),
                Color.FromRgb(254, 87, 34),
                Color.FromRgb(63, 81, 180),
                Color.FromRgb(204, 219, 57),
                Color.FromRgb(0, 149, 135),
                Color.FromRgb(76, 174, 80)
            };

            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnIsVisibleChanged;
            MouseWheel += MouseWheelOnRoll;
            Loaded += OnLoaded;
            TooltipTimeoutTimer.Tick += TooltipTimeoutTimerOnTick;

            DrawMargin.Background = Brushes.Transparent; // if this line is not set, then it does not detect mouse down event...
            DrawMargin.MouseDown += OnDraggingStart;
            DrawMargin.MouseUp += OnDraggingEnd;
        }

        static Chart()
        {
            Randomizer = new Random();
        }

        #endregion

        #region Debug
        public void MockIt(CoreSize size)
        {
            DisableAnimations = true;

            IsMocked = true;
            IsControlLoaded = true;

            Model.ControlSize = size;

            Model.DrawMargin.Height = Canvas.ActualHeight;
            Model.DrawMargin.Width = Canvas.ActualWidth;
        }

        public int GetCanvasElements()
        {
            return Canvas.Children.Count;
        }

        public int GetDrawMarginElements()
        {
            return DrawMargin.Children.Count;
        }

        public object GetCanvas()
        {
            return Canvas;
        }
        #endregion

        #region Essentials
        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            IsControlLoaded = true;
            Model.DrawMargin.Height = Canvas.ActualHeight;
            Model.DrawMargin.Width = Canvas.ActualWidth;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
#if DEBUG
            Debug.WriteLine("ChartResized");
#endif
            Model.ControlSize = new CoreSize(ActualWidth, ActualHeight);

            Model.Updater.Run();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs)
        {
#if DEBUG
            Debug.WriteLine("ChartVisibilityChanged");
#endif
            Model.ControlSize = new CoreSize(ActualWidth, ActualHeight);

            Model.Updater.Run();
            PrepareScrolBar();
        }

        private static void OnSeriesChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var chart = (Chart) o;

            if (chart.Series != null)
            {
                chart.Series.Chart = chart.Model;
                foreach (var series in chart.Series) series.Model.Chart = chart.Model;
            }

            if (chart.LastKnownSeriesCollection != chart.Series && chart.LastKnownSeriesCollection != null)
            {
                foreach (var series in chart.LastKnownSeriesCollection)
                {
                    series.Erase(true);
                }
            }

            CallChartUpdater()(o, e);
            chart.LastKnownSeriesCollection = chart.Series;
        }

        internal void ChartUpdated()
        {
            if (UpdaterTick != null) UpdaterTick.Invoke();
        }
        #endregion

        #region Events
        /// <summary>
        /// The DataClick event is fired when a user click any data point
        /// </summary>
        public event DataClickHandler DataClick;

        /// <summary>
        /// This event is fired every time the chart updates.
        /// </summary>
        public event UpdaterTickHandler UpdaterTick;
        #endregion

        #region Properties

        private static Random Randomizer { get; set; }
        private SeriesCollection LastKnownSeriesCollection { get; set; }

        /// <summary>
        /// Gets or sets the chart current canvas
        /// </summary>
        protected Canvas Canvas { get; set; }
        internal Canvas DrawMargin { get; set; }

        /// <summary>
        /// Gets or sets whether charts must randomize the starting default series color.
        /// </summary>
        public static bool RandomizeStartingColor { get; set; }

        /// <summary>
        /// This property need to be true when unit testing
        /// </summary>
        public bool IsMocked { get; set; }

        /// <summary>
        /// Gets or sets the application level default series color list
        /// </summary>
        public static List<Color> Colors { get; set; }

        public static readonly DependencyProperty AxisYProperty = DependencyProperty.Register(
            "AxisY", typeof(AxesCollection), typeof(Chart),
            new PropertyMetadata(null, CallChartUpdater()));
        /// <summary>
        /// Gets or sets vertical axis
        /// </summary>
        public AxesCollection AxisY
        {
            get { return (AxesCollection)GetValue(AxisYProperty); }
            set { SetValue(AxisYProperty, value); }
        }

        public static readonly DependencyProperty AxisXProperty = DependencyProperty.Register(
            "AxisX", typeof(AxesCollection), typeof(Chart),
            new PropertyMetadata(null, CallChartUpdater()));
        /// <summary>
        /// Gets or sets horizontal axis
        /// </summary>
        public AxesCollection AxisX
        {
            get { return (AxesCollection)GetValue(AxisXProperty); }
            set { SetValue(AxisXProperty, value); }
        }

        public static readonly DependencyProperty ChartLegendProperty = DependencyProperty.Register(
            "ChartLegend", typeof(UserControl), typeof(Chart),
            new PropertyMetadata(null, CallChartUpdater()));
        /// <summary>
        /// Gets or sets the control to use as chart legend for this chart.
        /// </summary>
        public UserControl ChartLegend
        {
            get { return (UserControl)GetValue(ChartLegendProperty); }
            set { SetValue(ChartLegendProperty, value); }
        }

        public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
            "Zoom", typeof(ZoomingOptions), typeof(Chart),
            new PropertyMetadata(default(ZoomingOptions)));
        /// <summary>
        /// Gets or sets chart zoom behavior
        /// </summary>
        public ZoomingOptions Zoom
        {
            get { return (ZoomingOptions)GetValue(ZoomProperty); }
            set { SetValue(ZoomProperty, value); }
        }

        public static readonly DependencyProperty LegendLocationProperty = DependencyProperty.Register(
            "LegendLocation", typeof(LegendLocation), typeof(Chart),
            new PropertyMetadata(LegendLocation.None, CallChartUpdater()));
        /// <summary>
        /// Gets or sets where legend is located
        /// </summary>
        public LegendLocation LegendLocation
        {
            get { return (LegendLocation)GetValue(LegendLocationProperty); }
            set { SetValue(LegendLocationProperty, value); }
        }

        public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
            "Series", typeof(SeriesCollection), typeof(Chart),
            new PropertyMetadata(default(SeriesCollection), OnSeriesChanged));

        /// <summary>
        /// Gets or sets chart series collection to plot.
        /// </summary>
        public SeriesCollection Series
        {
            get { return (SeriesCollection)GetValue(SeriesProperty); }
            set { SetValue(SeriesProperty, value); }
        }

        public static readonly DependencyProperty AnimationsSpeedProperty = DependencyProperty.Register(
            "AnimationsSpeed", typeof(TimeSpan), typeof(Chart),
            new PropertyMetadata(default(TimeSpan), UpdateChartFrequency));

        /// <summary>
        /// Gets or sets the default animation speed for this chart, you can override this speed for each element (series and axes)
        /// </summary>
        public TimeSpan AnimationsSpeed
        {
            get { return (TimeSpan)GetValue(AnimationsSpeedProperty); }
            set { SetValue(AnimationsSpeedProperty, value); }
        }

        public static readonly DependencyProperty DisableAnimationsProperty = DependencyProperty.Register(
            "DisableAnimations", typeof(bool), typeof(Chart),
            new PropertyMetadata(default(bool), UpdateChartFrequency));
        /// <summary>
        /// Gets or sets if the chart is animated or not.
        /// </summary>
        public bool DisableAnimations
        {
            get { return (bool)GetValue(DisableAnimationsProperty); }
            set { SetValue(DisableAnimationsProperty, value); }
        }

        public static readonly DependencyProperty DataTooltipProperty = DependencyProperty.Register(
            "DataTooltip", typeof(UserControl), typeof(Chart), new PropertyMetadata(null));
        /// <summary>
        /// Gets or sets the chart data tooltip.
        /// </summary>
        public UserControl DataTooltip
        {
            get { return (UserControl)GetValue(DataTooltipProperty); }
            set { SetValue(DataTooltipProperty, value); }
        }

        public static readonly DependencyProperty HoverableProperty = DependencyProperty.Register(
            "Hoverable", typeof(bool), typeof(Chart), new PropertyMetadata(true));

        /// <summary>
        /// gets or sets whether chart should react when a user moves the mouse over a data point.
        /// </summary>
        public bool Hoverable
        {
            get { return (bool) GetValue(HoverableProperty); }
            set { SetValue(HoverableProperty, value); }
        }

        public static readonly DependencyProperty ScrollModeProperty = DependencyProperty.Register(
            "ScrollMode", typeof(ScrollMode), typeof(Chart), 
            new PropertyMetadata(ScrollMode.None, ScrollModeOnChanged));
        /// <summary>
        /// Gets or sets chart scroll mode
        /// </summary>
        public ScrollMode ScrollMode
        {
            get { return (ScrollMode) GetValue(ScrollModeProperty); }
            set { SetValue(ScrollModeProperty, value); }
        }

        public static readonly DependencyProperty ScrollHorizontalFromProperty = DependencyProperty.Register(
            "ScrollHorizontalFrom", typeof(double), typeof(Chart), 
            new PropertyMetadata(default(double), ScrollLimitOnChanged));
        /// <summary>
        /// Gets or sets the scrolling horizontal start value
        /// </summary>
        public double ScrollHorizontalFrom
        {
            get { return (double) GetValue(ScrollHorizontalFromProperty); }
            set { SetValue(ScrollHorizontalFromProperty, value); }
        }

        public static readonly DependencyProperty ScrollHorizontalToProperty = DependencyProperty.Register(
            "ScrollHorizontalTo", typeof(double), typeof(Chart), 
            new PropertyMetadata(default(double),  ScrollLimitOnChanged));
        /// <summary>
        /// Gets or sets the scrolling horizontal end value
        /// </summary>
        public double ScrollHorizontalTo
        {
            get { return (double) GetValue(ScrollHorizontalToProperty); }
            set { SetValue(ScrollHorizontalToProperty, value); }
        }

        public static readonly DependencyProperty ScrollVerticalFromProperty = DependencyProperty.Register(
            "ScrollVerticalFrom", typeof(double), typeof(Chart), new PropertyMetadata(default(double)));
        /// <summary>
        /// Gets or sets the scrolling vertical start value
        /// </summary>
        public double ScrollVerticalFrom
        {
            get { return (double) GetValue(ScrollVerticalFromProperty); }
            set { SetValue(ScrollVerticalFromProperty, value); }
        }

        public static readonly DependencyProperty ScrollVerticalToProperty = DependencyProperty.Register(
            "ScrollVerticalTo", typeof(double), typeof(Chart), new PropertyMetadata(default(double)));
        /// <summary>
        /// Gets or sets the scrolling vertical end value
        /// </summary>
        public double ScrollVerticalTo
        {
            get { return (double) GetValue(ScrollVerticalToProperty); }
            set { SetValue(ScrollVerticalToProperty, value); }
        }

        public static readonly DependencyProperty ScrollBarFillProperty = DependencyProperty.Register(
            "ScrollBarFill", typeof(Brush), typeof(Chart), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(30, 30, 30, 30))));
        /// <summary>
        /// Gets or sets the scroll bar fill brush
        /// </summary>
        public Brush ScrollBarFill
        {
            get { return (Brush) GetValue(ScrollBarFillProperty); }
            set { SetValue(ScrollBarFillProperty, value); }
        }

        public static readonly DependencyProperty UpdaterStateProperty = DependencyProperty.Register(
            "UpdaterState", typeof(UpdaterState), typeof(Chart), 
            new PropertyMetadata(default(UpdaterState), CallChartUpdater()));
        /// <summary>
        /// Gets or sets chart's updater state
        /// </summary>
        public UpdaterState UpdaterState
        {
            get { return (UpdaterState) GetValue(UpdaterStateProperty); }
            set { SetValue(UpdaterStateProperty, value); }
        }

        /// <summary>
        /// Gets the chart model, the model is who calculates everything, is the engine of the chart
        /// </summary>
        public ChartCore Model
        {
            get { return ChartCoreModel; }
        }

        /// <summary>
        /// Gets whether the chart has an active tooltip.
        /// </summary>
        public bool HasTooltip
        {
            get { return DataTooltip != null; }
        }

        /// <summary>
        /// Gets whether the chart has a DataClick event attacked.
        /// </summary>
        public bool HasDataClickEventAttached
        {
            get { return DataClick != null; }
        }

        /// <summary>
        /// Gets whether the chart is already loaded in the view.
        /// </summary>
        public bool IsControlLoaded { get; private set; }

        /// <summary>
        /// Gets whether the control is in design mode
        /// </summary>
        public bool IsInDesignMode { get { return DesignerProperties.GetIsInDesignMode(this); } }

        /// <summary>
        /// Gets the visible series in the chart
        /// </summary>
        public IEnumerable<ISeriesView> ActualSeries
        {
            get
            {
                if (DesignerProperties.GetIsInDesignMode(this) && Series == null)
                    SetValue(SeriesProperty, GetDesignerModeCollection());

                return (Series ?? Enumerable.Empty<ISeriesView>())
                    .Where(x => x.IsSeriesVisible);
            }
        }

        #endregion

        #region Public Methods

        public void SetDrawMarginTop(double value)
        {
            Canvas.SetTop(DrawMargin, value);
        }

        public void SetDrawMarginLeft(double value)
        {
            Canvas.SetLeft(DrawMargin, value);
        }

        public void SetDrawMarginHeight(double value)
        {
            DrawMargin.Height = value;
        }

        public void SetDrawMarginWidth(double value)
        {
            DrawMargin.Width = value;
        }

        public void AddToView(object element)
        {
            var wpfElement = (FrameworkElement) element;
            if (wpfElement == null) return;
            Canvas.Children.Add(wpfElement);
        }

        public void AddToDrawMargin(object element)
        {
            var wpfElement = (FrameworkElement)element;
            if (wpfElement == null) return;
            DrawMargin.Children.Add(wpfElement);
        }

        public void RemoveFromView(object element)
        {
            var wpfElement = (FrameworkElement)element;
            if (wpfElement == null) return;
            Canvas.Children.Remove(wpfElement);
        }

        public void RemoveFromDrawMargin(object element)
        {
            var wpfElement = (FrameworkElement)element;
            if (wpfElement == null) return;
            DrawMargin.Children.Remove(wpfElement);
        }

        public void EnsureElementBelongsToCurrentView(object element)
        {
            var wpfElement = (FrameworkElement)element;
            if (wpfElement == null) return;
            var p = (Canvas)wpfElement.Parent;
            if (p != null) p.Children.Remove(wpfElement);
            AddToView(wpfElement);
        }

        public void EnsureElementBelongsToCurrentDrawMargin(object element)
        {
            var wpfElement = (FrameworkElement)element;
            if (wpfElement == null) return;
            var p = (Canvas)wpfElement.Parent;
            if (p != null) p.Children.Remove(wpfElement);
            AddToDrawMargin(wpfElement);
        }

        public void ShowLegend(CorePoint at)
        {
            if (ChartLegend == null) return;

            if (ChartLegend.Parent == null)
            {
                AddToView(ChartLegend);
                Canvas.SetLeft(ChartLegend, 0d);
                Canvas.SetTop(ChartLegend, 0d);
            }

            ChartLegend.Visibility = Visibility.Visible;

            Canvas.SetLeft(ChartLegend, at.X);
            Canvas.SetTop(ChartLegend, at.Y);
        }

        public void HideLegend()
        {
            if (ChartLegend != null)
                ChartLegend.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Forces the chart to update
        /// </summary>
        /// <param name="restartView">Indicates whether the update should restart the view, animations will run again if true.</param>
        /// <param name="force">Force the updater to run when called, without waiting for the next updater step.</param>
        public void Update(bool restartView = false, bool force = false)
        {
            if (Model != null) Model.Updater.Run(restartView, force);
        }

        public List<AxisCore> MapXAxes(ChartCore chart)
        {
            if (DesignerProperties.GetIsInDesignMode(this) && AxisX == null)
                AxisX = DefaultAxes.DefaultAxis;

            if (AxisX.Count == 0) AxisX.AddRange(DefaultAxes.CleanAxis);
            return AxisX.Select(x =>
            {
                if (x.Parent == null)
                {
                    if (x.Separator != null) chart.View.AddToView(x.Separator);
                    chart.View.AddToView(x);
                }
                return x.AsCoreElement(Model, AxisOrientation.X);
            }).ToList();
        }

        public List<AxisCore> MapYAxes(ChartCore chart)
        {
            if (DesignerProperties.GetIsInDesignMode(this) && AxisY == null)
                AxisY = DefaultAxes.DefaultAxis;

            if (AxisY.Count == 0) AxisY.AddRange(DefaultAxes.DefaultAxis);
            return AxisY.Select(x =>
            {
                if (x.Parent == null) chart.View.AddToView(x);
                return x.AsCoreElement(Model, AxisOrientation.Y);
            }).ToList();
        }

        public Color GetNextDefaultColor()
        {
            if (Series.CurrentSeriesIndex == int.MaxValue) Series.CurrentSeriesIndex = 0;
            var i = Series.CurrentSeriesIndex;
            Series.CurrentSeriesIndex++;
            var r = RandomizeStartingColor ? Randomizer.Next(0, Colors.Count) : 0;
            return Colors[(i + r)%Colors.Count];
        }
        #endregion

        #region Tooltip and legend
        private static DispatcherTimer TooltipTimeoutTimer { get; set; }
        private static UserControl ActiveTooltip { get; set; }

        public static readonly DependencyProperty TooltipTimeoutProperty = DependencyProperty.Register(
            "TooltipTimeout", typeof(TimeSpan), typeof(Chart),
            new PropertyMetadata(default(TimeSpan), TooltipTimeoutCallback));
        /// <summary>
        /// Gets or sets the time a tooltip takes to hide when the user leaves the data point.
        /// </summary>
        public TimeSpan TooltipTimeout
        {
            get { return (TimeSpan)GetValue(TooltipTimeoutProperty); }
            set { SetValue(TooltipTimeoutProperty, value); }
        }

        internal void AttachHoverableEventTo(FrameworkElement element)
        {
            return;

            element.MouseDown -= DataMouseDown;
            element.MouseEnter -= DataMouseEnter;
            element.MouseLeave -= DataMouseLeave;

            element.MouseDown += DataMouseDown;
            element.MouseEnter += DataMouseEnter;
            element.MouseLeave += DataMouseLeave;
        }

        private void DataMouseDown(object sender, MouseEventArgs e)
        {
            var result = ActualSeries.SelectMany(x => x.ActualValues.GetPoints(x))
                .FirstOrDefault(x =>
                {
                    var pointView = x.View as PointView;
                    return pointView != null && Equals(pointView.HoverShape, sender);
                });
            if (DataClick != null) DataClick.Invoke(sender, result);
        }

        private void DataMouseEnter(object sender, EventArgs e)
        {
            TooltipTimeoutTimer.Stop();

            var source = ActualSeries.SelectMany(x => x.ActualValues.GetPoints(x)).ToList();
            var senderPoint = source.FirstOrDefault(x => x.View != null &&
                                                         Equals(((PointView) x.View).HoverShape, sender));

            if (senderPoint == null) return;

            if (Hoverable) senderPoint.View.OnHover(senderPoint);

            if (DataTooltip != null)
            {
                if (DataTooltip.Parent == null)
                {
                    Panel.SetZIndex(DataTooltip, int.MaxValue);
                    AddToView(DataTooltip);
                    Canvas.SetTop(DataTooltip, 0d);
                    Canvas.SetLeft(DataTooltip, 0d);
                }

                ActiveTooltip = DataTooltip;

                var lcTooltip = DataTooltip as IChartTooltip;
                if (lcTooltip == null)
                    throw new LiveChartsException("The current tooltip is not valid, ensure it implements IChartsTooltip");

                if (lcTooltip.SelectionMode == null)
                    lcTooltip.SelectionMode = senderPoint.SeriesView.Model.PreferredSelectionMode;

                var coreModel = ChartFunctions.GetTooltipData(senderPoint, Model,lcTooltip.SelectionMode.Value);

                lcTooltip.Data = new TooltipData
                {
                    XFormatter = coreModel.XFormatter,
                    YFormatter = coreModel.YFormatter,
                    SharedValue = coreModel.Shares,
                    SelectionMode = lcTooltip.SelectionMode ?? TooltipSelectionMode.OnlySender,
                    Points = coreModel.Points.Select(x => new DataPointViewModel
                    {
                        Series = new SeriesViewModel
                        {
                            PointGeometry = ((Series) x.SeriesView).PointGeometry ??
                                            Geometry.Parse("M 0,0.5 h 1,0.5 Z"),
                            Fill = ((Series) x.SeriesView) is IFondeable &&
                                   !(x.SeriesView is IVerticalStackedAreaSeriesView ||
                                     x.SeriesView is IStackedAreaSeriesView)
                                ? ((IFondeable) x.SeriesView).PointForeround
                                : ((Series) x.SeriesView).Fill,
                            Stroke = ((Series) x.SeriesView).Stroke,
                            StrokeThickness = ((Series) x.SeriesView).StrokeThickness,
                            Title = ((Series) x.SeriesView).Title,
                        },
                        ChartPoint = x
                    }).ToList()
                };

                DataTooltip.Visibility = Visibility.Visible;
                DataTooltip.UpdateLayout();

                var location = GetTooltipPosition(senderPoint);
                location = new Point(Canvas.GetLeft(DrawMargin) + location.X, Canvas.GetTop(DrawMargin) + location.Y);
                if (lcTooltip.IsWrapped)
                {
                    var container = (FrameworkElement) DataTooltip.Parent;
                    var positionTransform = TransformToAncestor(container);
                    var pos = positionTransform.Transform(new Point(0, 0));

                    location.X += pos.X;
                    location.Y += pos.Y;

                    if (location.X < 0) location.X = 0;
                    if (location.X + DataTooltip.ActualWidth > container.ActualWidth)
                    {
                        var dif = container.ActualWidth - (location.X + DataTooltip.ActualWidth);
                        dif *= container.ActualWidth/2 > senderPoint.ChartLocation.X ? 1 : -1;
                        location.X += dif;
                    }
                }

                if (DisableAnimations)
                {
                    Canvas.SetLeft(DataTooltip, location.X);
                    Canvas.SetTop(DataTooltip, location.Y);
                }
                else
                {
                    DataTooltip.BeginAnimation(Canvas.LeftProperty,
                        new DoubleAnimation(location.X, TimeSpan.FromMilliseconds(200)));
                    DataTooltip.BeginAnimation(Canvas.TopProperty,
                        new DoubleAnimation(location.Y, TimeSpan.FromMilliseconds(200)));
                }
            }
        }

        private void DataMouseLeave(object sender, EventArgs e)
        {
            TooltipTimeoutTimer.Stop();
            TooltipTimeoutTimer.Start();

            var source = ActualSeries.SelectMany(x => x.ActualValues.GetPoints(x));
            var senderPoint = source.FirstOrDefault(x => x.View != null &&
                                                         Equals(((PointView) x.View).HoverShape, sender));

            if (senderPoint == null) return;

            if (Hoverable) senderPoint.View.OnHoverLeave(senderPoint);
        }

        private void TooltipTimeoutTimerOnTick(object sender, EventArgs eventArgs)
        {
            TooltipTimeoutTimer.Stop();
            if (ActiveTooltip == null) return;
            ActiveTooltip.Visibility = Visibility.Hidden;
        }

        public CoreSize LoadLegend()
        {
            if (ChartLegend == null || LegendLocation == LegendLocation.None)
                return new CoreSize();

            if (ChartLegend.Parent == null)
                Canvas.Children.Add(ChartLegend);

            var l = new List<SeriesViewModel>();

            foreach (var t in ActualSeries)
            {
                var item = new SeriesViewModel();

                var series = (Series)t;

                item.Title = series.Title;
                item.StrokeThickness = series.StrokeThickness;
                item.Stroke = series.Stroke;
                item.Fill = ((Series) t) is IFondeable &&
                            !(t is IVerticalStackedAreaSeriesView ||
                              t is IStackedAreaSeriesView)
                    ? ((IFondeable) t).PointForeround
                    : ((Series) t).Fill;
                item.PointGeometry = series.PointGeometry ?? Geometry.Parse("M 0,0.5 h 1,0.5 Z");

                l.Add(item);
            }

            var iChartLegend = ChartLegend as IChartLegend;
            if (iChartLegend == null)
                throw new LiveChartsException("The current legend is not valid, ensure it implements IChartLegend");

            iChartLegend.Series = l;

            var defaultLegend = ChartLegend as DefaultLegend;
            if (defaultLegend != null)
            {
                defaultLegend.InternalOrientation = LegendLocation == LegendLocation.Bottom ||
                                                    LegendLocation == LegendLocation.Top
                    ? Orientation.Horizontal
                    : Orientation.Vertical;

                defaultLegend.MaxWidth = defaultLegend.InternalOrientation == Orientation.Horizontal
                    ? ActualWidth
                    : double.PositiveInfinity;

                defaultLegend.MaxHeight = defaultLegend.InternalOrientation == Orientation.Vertical
                    ? ActualHeight
                    : double.PositiveInfinity;
            }

            ChartLegend.UpdateLayout();
            ChartLegend.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            return new CoreSize(ChartLegend.DesiredSize.Width,
                ChartLegend.DesiredSize.Height);
        }

        private static void TooltipTimeoutCallback(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs)
        {
            var chart = (Chart)dependencyObject;

            if (chart == null) return;

            TooltipTimeoutTimer.Interval = chart.TooltipTimeout;
        }

        public void HideTooltop()
        {
            if (DataTooltip == null) return;

            DataTooltip.Visibility = Visibility.Hidden;
        }

        protected virtual Point GetTooltipPosition(ChartPoint senderPoint)
        {
            var xt = senderPoint.ChartLocation.X;
            var yt = senderPoint.ChartLocation.Y;

            xt = xt > DrawMargin.Width / 2 ? xt - DataTooltip.ActualWidth - 5 : xt + 5;
            yt = yt > DrawMargin.Height / 2 ? yt - DataTooltip.ActualHeight - 5 : yt + 5;

            return new Point(xt, yt);
        }

        private SeriesCollection GetDesignerModeCollection()
        {
            var r = new Random();
            SeriesCollection mockedCollection;

            if (this is PieChart)
            {

                mockedCollection = new SeriesCollection
                {
                    new PieSeries
                    {
                        Values = new ChartValues<ObservableValue> {new ObservableValue(r.Next(10, 100)) }
                    },
                    new PieSeries
                    {
                        Values = new ChartValues<ObservableValue> {new ObservableValue(r.Next(10, 100)) }
                    },
                    new PieSeries
                    {
                        Values = new ChartValues<ObservableValue> {new ObservableValue(r.Next(10, 100)) }
                    },
                    new PieSeries
                    {
                        Values = new ChartValues<ObservableValue> {new ObservableValue(r.Next(10, 100)) }
                    }
                };
            }
            else
            {
                Func<int, int,ChartValues<ObservableValue>> getRandomValues =
                    (from, to) => new ChartValues<ObservableValue>
                    {
                        new ObservableValue(r.Next(from, to)),
                        new ObservableValue(r.Next(from, to)),
                        new ObservableValue(r.Next(from, to)),
                        new ObservableValue(r.Next(from, to)),
                        new ObservableValue(r.Next(from, to))
                    };

                mockedCollection = new SeriesCollection
                {
                    new LineSeries {Values = getRandomValues(0, 100)},
                    new LineSeries {Values = getRandomValues(0, 100)},
                    new LineSeries {Values = getRandomValues(0, 100), Fill = Brushes.Transparent}
                };
            }

            var goWild = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            goWild.Tick += (sender, args) =>
            {
                foreach (var series in mockedCollection)
                {
                    foreach (ObservableValue chartValue in series.Values)
                    {
                        chartValue.Value = r.Next(10, 100);
                    }
                }
            };
            //not today... maybe later
            //goWild.Start();

            return mockedCollection;
        }
        #endregion

        #region Zooming and Panning
        private Point DragOrigin { get; set; }

        private void MouseWheelOnRoll(object sender, MouseWheelEventArgs e)
        {
            if (Zoom == ZoomingOptions.None) return;

            var p = e.GetPosition(this);

            var corePoint = new CorePoint(p.X, p.Y);

            e.Handled = true;

            if (e.Delta > 0)
                Model.ZoomIn(corePoint);
            else
                Model.ZoomOut(corePoint);
        }

        private void OnDraggingStart(object sender, MouseButtonEventArgs e)
        {
            if (Model == null || Model.AxisX == null || Model.AxisY == null) return;

            DragOrigin = e.GetPosition(this);
            DragOrigin = new Point(
                ChartFunctions.FromPlotArea(DragOrigin.X, AxisOrientation.X, Model),
                ChartFunctions.FromPlotArea(DragOrigin.Y, AxisOrientation.Y, Model));
        }

        private void OnDraggingEnd(object sender, MouseButtonEventArgs e)
        {
            if (Zoom == ZoomingOptions.None) return;

            var end = e.GetPosition(this);
            end = new Point(
                ChartFunctions.FromPlotArea(end.X, AxisOrientation.X, Model),
                ChartFunctions.FromPlotArea(end.Y, AxisOrientation.Y, Model));

            Model.Drag(new CorePoint(DragOrigin.X - end.X, DragOrigin.Y - end.Y));
        }
        #endregion

        #region ScrollBar functionality
        private bool _isDragging;
        private Point _previous;
        private Rectangle ScrollBar { get; set; }

        internal void PrepareScrolBar()
        {
            if (!IsControlLoaded) return;

            if (ScrollMode == ScrollMode.None)
            {
                RemoveFromDrawMargin(ScrollBar);
                ScrollBar = null;

                return;
            }

            if (ScrollBar == null)
            {
                ScrollBar = new Rectangle();

                ScrollBar.SetBinding(Shape.FillProperty,
                    new Binding {Path = new PropertyPath(ScrollBarFillProperty), Source = this});

                EnsureElementBelongsToCurrentView(ScrollBar);
                ScrollBar.MouseDown += ScrollBarOnMouseDown;
                MouseMove += ScrollBarOnMouseMove;
                ScrollBar.MouseUp += ScrollBarOnMouseUp;
            }

            ScrollBar.SetBinding(HeightProperty,
                new Binding {Path = new PropertyPath(ActualHeightProperty), Source = this});
            ScrollBar.SetBinding(WidthProperty,
                new Binding {Path = new PropertyPath(ActualWidthProperty), Source = this});

            var f = this.ConvertToPixels(new Point(ScrollHorizontalFrom, ScrollVerticalFrom));
            var t = this.ConvertToPixels(new Point(ScrollHorizontalTo, ScrollVerticalTo));

            if (ScrollMode == ScrollMode.X || ScrollMode == ScrollMode.XY)
            {
                Canvas.SetLeft(ScrollBar, f.X);
                if (t.X - f.X >= 0) ScrollBar.Width = t.X - f.X;
            }

            if (ScrollMode == ScrollMode.Y || ScrollMode == ScrollMode.XY)
            {
                Canvas.SetTop(ScrollBar, t.Y);
                if (f.Y - t.Y >= 0) ScrollBar.Height = f.Y - t.Y;
            }
        }

        private void ScrollBarOnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void ScrollBarOnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var d = e.GetPosition(this);

            var dp = new Point(d.X - _previous.X, d.Y - _previous.Y);
            var d0 = this.ConvertToChartValues(new Point());
            var d1 = this.ConvertToChartValues(dp);
            var dv = new Point(d0.X - d1.X, d0.Y - d1.Y);

            _previous = d;

            if (ScrollMode == ScrollMode.X || ScrollMode == ScrollMode.XY)
            {
                if (Math.Abs(dp.X) < 0.1) return;
                ScrollHorizontalFrom -= dv.X;
                ScrollHorizontalTo -= dv.X;
            }

            if (ScrollMode == ScrollMode.Y || ScrollMode == ScrollMode.XY)
            {
                if (Math.Abs(dp.Y) < 0.1) return;
                ScrollVerticalFrom += dv.Y;
                ScrollVerticalTo += dv.Y;
            }
        }

        private void ScrollBarOnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _previous = e.GetPosition(this);

            
        }

        private static void ScrollModeOnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var wpfChart = (Chart) o;
            if (o == null) return;
            wpfChart.PrepareScrolBar();
        }

        private static void ScrollLimitOnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var wpfChart = (Chart)o;
            if (o == null) return;
            wpfChart.PrepareScrolBar();
        }

        #endregion

        #region Property Changed
        /// <summary>
        /// Calls the chart updater
        /// </summary>
        /// <param name="animate">if true, the series view will be removed and added again, this restarts animations also.</param>
        /// <param name="updateNow">forces the updater to run as this function is called.</param>
        /// <returns></returns>
        protected static PropertyChangedCallback CallChartUpdater(bool animate = false, bool updateNow = false)
        {
            return (o, args) =>
            {
                var wpfChart = o as Chart;
                if (wpfChart == null) return;
                if (wpfChart.Model != null) wpfChart.Model.Updater.Run(animate, updateNow);
            };
        }

        private static void UpdateChartFrequency(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var wpfChart = (Chart) o;

            var freq = wpfChart.DisableAnimations ? TimeSpan.FromMilliseconds(10) : wpfChart.AnimationsSpeed;

            if (wpfChart.Model == null || wpfChart.Model.Updater == null) return;

            wpfChart.Model.Updater.UpdateFrequency(freq);

            CallChartUpdater(true)(o, e);
        }

        #endregion
    }
}
