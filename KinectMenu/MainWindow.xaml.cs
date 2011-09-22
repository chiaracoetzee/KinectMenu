// Kinect skeletal tracking
// for CS260 hw2 with Derrick
// Author: peggychi
// Latest Update: 09/21/2011

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

// ** for Kinect component **
using Microsoft.Research.Kinect.Nui;

// for UI interaction
using System.Windows.Threading;
using Coding4Fun.Kinect.Wpf;



namespace KinectMenu
{
    public static class DateTimeExtensions
    {
        public static long GetTimestamp(this DateTime value)
        {
            return Convert.ToInt64(value.ToString("yyyyMMddHHmmssff"));
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ** for Kinect component **
        Runtime nui = new Runtime();
        int window_height = 0, window_width = 0;

        // for UI testing
        Dictionary<Button, Location> original_location = new Dictionary<Button,Location>();
        Dictionary<Button, Canvas> submenu_map = new Dictionary<Button, Canvas>();
        Dictionary<Canvas, Canvas> parent_menu_map = new Dictionary<Canvas, Canvas>();
        Canvas active_menu;

        public MainWindow()
        {
            InitializeComponent();
            // get window height and width
            this.window_height = Convert.ToInt32(this.Height);
            this.window_width = Convert.ToInt32(this.Width);
            // set mouse cursor icon as hand
            this.Cursor = Cursors.Hand;
            
            // for UI testing

            this.active_menu = RootMenu;
            ButtonBack.Visibility = Visibility.Hidden;
            ButtonSelected.Visibility = Visibility.Hidden;

            // Build map of canvas name to canvas
            Dictionary<string, Canvas> canvas_by_name = new Dictionary<string,Canvas>();
            foreach (FrameworkElement element1 in LayoutRoot.Children)
            {
                Canvas canvas = element1 as Canvas;
                if (canvas == null) continue;
                canvas_by_name[canvas.Name] = canvas;
                canvas.Visibility = Visibility.Hidden;
            }

            // Set up all buttons
            foreach (FrameworkElement element1 in LayoutRoot.Children)
            {
                Canvas canvas = element1 as Canvas;
                if (canvas == null) continue;
                foreach (FrameworkElement element2 in canvas.Children)
                {
                    Button button = element2 as Button;
                    if (button == null) continue;

                    original_location[button] = GetLocation(button);
                    button.Click += delegate(object sender, RoutedEventArgs e) { component_click(sender as Button); };
                    button.MouseEnter += delegate(object sender, MouseEventArgs e) { component_enter(sender as Button); };
                    button.MouseLeave += delegate(object sender, MouseEventArgs e) { component_leave(sender as Button); };

                    Debug.Assert(button.Name.StartsWith("Button"));
                    string submenu_name = "Submenu" + button.Name.Substring("Button".Length);
                    if (canvas_by_name.ContainsKey(submenu_name))
                    {
                        submenu_map[button] = canvas_by_name[submenu_name];
                        parent_menu_map[canvas_by_name[submenu_name]] = canvas;
                    }
                }
            }

            MainCanvas.Visibility = Visibility.Visible;
            this.active_menu.Visibility = Visibility.Visible;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // ** for Kinect **
            if ((nui != null) && InitializeNui())
            {
                // add camera view
                nui.VideoFrameReady += new EventHandler<ImageFrameReadyEventArgs>(nui_ColorFrameReady);
                // event handler when skeleton is ready
                nui.SkeletonFrameReady += new EventHandler<SkeletonFrameReadyEventArgs>(nui_SkeletonFrameReady);
                // timestamps
                resetSwipeDetection();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // ** for Kinect **
            nui.Uninitialize();
        }

        // *************************** Kinect Initialization *************************** //

        Boolean nuiInitialized = false;

        private bool InitializeNui()
        {
            UninitializeNui();
            if (nui == null)
                return false;
            try
            {
                nui.Initialize(RuntimeOptions.UseSkeletalTracking | RuntimeOptions.UseColor);
            }
            catch (Exception _Exception)
            {
                Console.WriteLine(_Exception.ToString());
                return false;
            }

            // for camera view
            nui.VideoStream.Open(ImageStreamType.Video, 2, ImageResolution.Resolution640x480, ImageType.Color);
            
            // to reduce jitter
            nui.SkeletonEngine.TransformSmooth = true;
            var parameters = new TransformSmoothParameters
            {
                Smoothing = 0.75f,
                Correction = 0.0f,
                JitterRadius = 0.05f,
                MaxDeviationRadius = 0.04f
            };
            nui.SkeletonEngine.SmoothParameters = parameters;
            
            nuiInitialized = true;
            return true;
        }

        private void UninitializeNui()
        {
            if ((nui != null) && (nuiInitialized))
                nui.Uninitialize();
            nuiInitialized = false;
        }


        // *************************** Kinect skeleton and color events *************************** //

        void nui_SkeletonFrameReady(Object sender, SkeletonFrameReadyEventArgs e)
        {
            SkeletonFrame allSkeletons = e.SkeletonFrame;

            SkeletonData skeleton = (from s in allSkeletons.Skeletons
                                     where s.TrackingState == SkeletonTrackingState.Tracked
                                     select s).FirstOrDefault();

            if (skeleton != null)
            {
                // detect right hand only
                ui_detection(skeleton.Joints[JointID.HandRight]);
            }
        }

        void nui_ColorFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            // 32-bit per pixel, RGBA image
            PlanarImage Image = e.ImageFrame.Image;
            video.Source = BitmapSource.Create(
                Image.Width, Image.Height, 96, 96, 
                PixelFormats.Bgr32, null, Image.Bits, Image.Width * Image.BytesPerPixel);
        }

        // *************************** for Kinect event actions *************************** //

        // process the joint information (currently right hand only)
        private void ui_detection(Joint joint)
        {
            // scale to the UI window size
            joint = joint.ScaleTo(this.window_width, this.window_height, .5f, .5f);
            int handX = Convert.ToInt32(joint.Position.X), handY = Convert.ToInt32(joint.Position.Y);
            // set the mouse position same as user's right hand deteced by Kinect
            NativeMethods.SetCursorPos(handX, handY);
            // detect swipe
            detectSwipe(new Point(handX, handY));
        }

        private partial class NativeMethods
        {
            [System.Runtime.InteropServices.DllImportAttribute("user32.dll", EntryPoint = "SetCursorPos")]
            [return: System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool SetCursorPos(int X, int Y);
        }

        // ********************************* //
        //   hold and select using a timer   //
        // ********************************* //

        DispatcherTimer timer;
        int focus_time = 0; // user holds an item
        const int click_time = 2; // hold an item for 2 seconds to select
        Button focus_component = null;

        private void restartTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += timer_Tick;
            timer.Start();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            // for UI testing: show focus time
            this.textBlock_test.Text = "Focus: " + Convert.ToString(focus_time + 1) + " sec";

            focus_time++;
            if (focus_time >= click_time)
            {
                component_click(focus_component);
                resetTimer();
            }
        }

        private void resetTimer()
        {
            focus_time = 0;
            timer.Stop();
        }

        // ********************************* //
        //           swipe detection         //
        // ********************************* //

        long last_threshold_time;
        long last_detected_time;
        static int Threshold_detection = 20; // swipe within 0.2s
        static double x_swipe_distance = 0.4, y_swipe_distance = 0.3;
        Point last_threshold_position;
        // Queue<Point> mousePath = new Queue<Point>();
        
        private void resetSwipeDetection()
        {
            // mousePath = new Queue<Point>(); // refresh mouse path
            last_detected_time = getCurrentTimestamp();
            last_threshold_time = last_detected_time;
        }
        
        private void detectSwipe(Point currentHand)
        {
            long timeNow = getCurrentTimestamp();
            if (timeNow - last_detected_time > Threshold_detection)
            {
                // user hasn't moved for 1s, reset
                resetSwipeDetection();
                last_threshold_position = currentHand;
            }
            // push to the mouse path
            // mousePath.Enqueue(currentHand);
            if (timeNow - last_threshold_time > Threshold_detection)
            {
                // see if it's a swipt action: from right to left
                if (last_threshold_position.X - currentHand.X >= window_width * x_swipe_distance
                    && Math.Abs(last_threshold_position.Y - currentHand.Y) <= window_height * y_swipe_distance)
                {
                    // openPreviousLayer(game_button_list, main_button_list);
                    this.textBlock_test.Text = "Swipe! " + Convert.ToString(timeNow);
                }
                // update the position
                resetSwipeDetection();
                last_threshold_position = currentHand;
            }
            last_detected_time = timeNow;
        }

        private long getCurrentTimestamp()
        {
            return DateTime.Now.GetTimestamp();
        }

        // *************************** helpers *************************** //

        struct Location
        {
            public Location(double left, double top, double width, double height)
            {
                this.Left = left;
                this.Top = top;
                this.Width = width;
                this.Height = height;
            }
            public double Left;
            public double Top;
            public double Width;
            public double Height;
        }

        Location GetLocation(FrameworkElement e)
        {
            Location result = new Location();
            result.Left = (double)e.GetValue(LeftProperty);
            result.Top = (double)e.GetValue(TopProperty);
            result.Width = e.Width;
            result.Height = e.Height;
            return result;
        }

        void SetLocation(FrameworkElement e, Location loc)
        {
            e.SetValue(LeftProperty, loc.Left);
            e.SetValue(TopProperty, loc.Top);
            e.Width = loc.Width;
            e.Height = loc.Height;
        }

        // *************************** navigation interactions *************************** //

        bool animationInProgress = false;

        private void component_click(Button component)
        {
            if (animationInProgress)
            {
                return;
            }
            if (submenu_map.ContainsKey(component)) { // Opens submenu
                animationInProgress = true;
                component.SetValue(Canvas.ZIndexProperty, 1);
                animateButton(component, original_location[ButtonBack], new Duration(TimeSpan.FromSeconds(0.5)),
                              delegate(object sender, EventArgs e)
                {
                    animationInProgress = false;
                    openNextLayer(active_menu, submenu_map[component]);
                    SetLocation(component, original_location[component]);
                    component.SetValue(Canvas.ZIndexProperty, 0);
                });
            }
            else if (component == ButtonBack) { // back button
                openPreviousLayer(active_menu, parent_menu_map[active_menu]);
            }
            else {
                optionSelected(component);
            }

            // for UI testing
            this.textBlock_test.Text = "";
        }

        private void openRootLayer(Canvas currentMenu, Canvas rootMenu)
        {
            currentMenu.Visibility = Visibility.Hidden;
            rootMenu.Visibility = Visibility.Visible;

            // hide back area
            this.ButtonBack.Visibility = Visibility.Hidden;
            active_menu = rootMenu;
        }


        private void openNextLayer(Canvas currentMenu, Canvas nextMenu)
        {
            // hide previous layer and show next layer
            currentMenu.Visibility = Visibility.Hidden;
            nextMenu.Visibility = Visibility.Visible;

            // show back area
            this.ButtonBack.Visibility = Visibility.Visible;
            active_menu = nextMenu;
        }

        private void openPreviousLayer(Canvas currentMenu, Canvas previousMenu)
        {
            // hide current layer and show previous layer
            currentMenu.Visibility = Visibility.Hidden;
            previousMenu.Visibility = Visibility.Visible;

            if (previousMenu == RootMenu)
            {
                // hide back area
                this.ButtonBack.Visibility = Visibility.Hidden;
            }
            active_menu = previousMenu;
        }

        private void optionSelected(Button component)
        {
            component.SetValue(Canvas.ZIndexProperty, 1);
            animationInProgress = true;
            animateButton(component, original_location[ButtonSelected], new Duration(TimeSpan.FromSeconds(0.5)),
                          delegate(object sender, EventArgs e)
                          {
                              SetLocation(component, original_location[ButtonSelected]);
                              animateButton(component, original_location[ButtonSelected], new Duration(TimeSpan.FromSeconds(2.0)),
                                            delegate(object sender2, EventArgs e2)
                                            {
                                                animationInProgress = false;
                                                openRootLayer(active_menu, RootMenu);
                                                SetLocation(component, original_location[component]);
                                                component.SetValue(Canvas.ZIndexProperty, 0);
                                            }); 
                          });
        }

        private void component_enter(Button component)
        {
            if (focus_component == null || focus_component.Name != component.Name)
            {
                focus_component = component;
                restartTimer();

                if (!animationInProgress)
                {
                    Location loc = original_location[component];
                    Location zoomedLoc = new Location(loc.Left - 50, loc.Top - 50, loc.Width + 100, loc.Height + 100);
                    component.SetValue(Canvas.ZIndexProperty, 1);
                    animateButton(component, zoomedLoc, new Duration(TimeSpan.FromSeconds(0.25)), delegate(object sender, EventArgs e) { SetLocation(component, zoomedLoc); });
                }
            }
        }

        private void component_leave(Button component)
        {
            resetTimer();
            focus_component = null;
            if (!animationInProgress)
            {
                Location loc = original_location[component];
                component.SetValue(Canvas.ZIndexProperty, 0);
                animateButton(component, loc, new Duration(TimeSpan.FromSeconds(0.25)), delegate(object sender, EventArgs e) { SetLocation(component, loc); });
            }
        }

        // *************************** animations *********************************** //

        private void animateButton(Button button, Location loc, Duration duration, EventHandler onCompleted)
        {
            this.RegisterName(button.Name, button);

            DoubleAnimation leftAnim = new DoubleAnimation(loc.Left, duration);
            DoubleAnimation topAnim = new DoubleAnimation(loc.Top, duration);
            DoubleAnimation widthAnim = new DoubleAnimation(loc.Width, duration);
            DoubleAnimation heightAnim = new DoubleAnimation(loc.Height, duration);

            Storyboard.SetTargetProperty(leftAnim, new PropertyPath(Window.LeftProperty));
            Storyboard.SetTargetProperty(topAnim, new PropertyPath(Window.TopProperty));
            Storyboard.SetTargetProperty(widthAnim, new PropertyPath(FrameworkElement.WidthProperty));
            Storyboard.SetTargetProperty(heightAnim, new PropertyPath(FrameworkElement.HeightProperty));
            Storyboard storyboard = new Storyboard();
            foreach (DoubleAnimation anim in new DoubleAnimation[] { leftAnim, topAnim, widthAnim, heightAnim })
            {
                anim.FillBehavior = FillBehavior.Stop;
                Storyboard.SetTargetName(anim, button.Name);
                storyboard.Children.Add(anim);
            }
            leftAnim.Completed += delegate(object sender, EventArgs e)
            {
                storyboard.Stop();
            };
            if (onCompleted != null)
            {
                leftAnim.Completed += onCompleted;
            }
            storyboard.Begin(button);

        }
    }
}
