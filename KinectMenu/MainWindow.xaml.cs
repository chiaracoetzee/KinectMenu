// Kinect skeletal tracking
// for CS260 hw2 with Derrick
// Author: peggychi and dcoetzee (at) eecs.berkeley.edu
// Latest Update: 09/22/2011

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using System.Windows.Resources;
using System.Windows.Shapes;

// ** for Kinect component **
using Microsoft.Research.Kinect.Nui;

// for UI interaction
using System.Windows.Threading;
using Coding4Fun.Kinect.Wpf;


namespace KinectMenu
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    
    // user interface
    public partial class MainWindow : Window
    {
        // ** for Kinect component **
        Runtime nui = new Runtime();
        int window_height = 0, window_width = 0;

        // for UI items
        Dictionary<Button, Location> original_location = new Dictionary<Button,Location>();
        Dictionary<Button, Canvas> submenu_map = new Dictionary<Button, Canvas>();
        Dictionary<Canvas, Canvas> parent_menu_map = new Dictionary<Canvas, Canvas>();
        Canvas active_menu;

        // for testing
        Boolean displayMsg = false;
        Boolean detectKinectSwipe = true;
        Boolean detectKinectPush = true;

        public MainWindow()
        {
            InitializeComponent();
            // get window height and width
            this.window_height = Convert.ToInt32(this.Height);
            this.window_width = Convert.ToInt32(this.Width);
            // set mouse cursor icon as hand
            this.Cursor = Cursors.Hand;
            
            // for UI
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

                    Uri contentUriJpg = new Uri("/" + button.Name + ".jpg", UriKind.Relative);
                    StreamResourceInfo resourceInfoJpg = Application.GetContentStream(contentUriJpg);
                    Uri contentUriPng = new Uri("/" + button.Name + ".png", UriKind.Relative);
                    StreamResourceInfo resourceInfoPng = Application.GetContentStream(contentUriPng);
                    if (resourceInfoJpg != null || resourceInfoPng != null)
                    {
                        Image image = new Image();
                        image.Source = new BitmapImage(resourceInfoJpg != null ? contentUriJpg : contentUriPng);
                        button.Content = image;
                    }

                    if (canvas.Name.StartsWith("SubmenuAlbum"))
                    {
                        // It's a song, add the music note background
                        Grid contentGrid = new Grid();
                        Image backgroundImage = new Image();
                        backgroundImage.Source = new BitmapImage(new Uri("/NoteBackground.png", UriKind.Relative));
                        // backgroundImage.SetValue(LeftProperty, 0.0);
                        // backgroundImage.SetValue(TopProperty, 0.0);
                        contentGrid.Children.Add(backgroundImage);

                        Label label = new Label();
                        label.Content = (string)button.Content;
                        label.FontSize = 50;
                        label.HorizontalAlignment = HorizontalAlignment.Center;
                        label.VerticalAlignment = VerticalAlignment.Center;
                        contentGrid.Children.Add(label);
                        Viewbox viewbox = new Viewbox();
                        viewbox.Child = contentGrid;
                        button.Content = viewbox;
                    }
                }
            }

            Image mainBackgroundImage = new Image();
            mainBackgroundImage.Source = new BitmapImage(new Uri("/background.jpg", UriKind.Relative));
            mainBackgroundImage.SetValue(Canvas.ZIndexProperty, -1);
            MainCanvas.Children.Add(mainBackgroundImage);

            MainCanvas.Visibility = Visibility.Visible;
            this.active_menu.Visibility = Visibility.Visible;
        }

        static double mouseScaleX = 1, mouseScaleY = 1;
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // ** for Kinect **
            if ((nui != null) && InitializeNui())
            {
                // add camera view
                nui.VideoFrameReady += new EventHandler<ImageFrameReadyEventArgs>(nui_monoFrameReady);
                // event handler when skeleton is ready
                nui.SkeletonFrameReady += new EventHandler<SkeletonFrameReadyEventArgs>(nui_SkeletonFrameReady);
                // for depth image
                nui.DepthFrameReady += new EventHandler<ImageFrameReadyEventArgs>(nui_DepthFrameReady);

                // timestamps
                resetSwipeDetection();
                // mouse scale
                mouseScaleX = (double)320 / (double)this.window_width;
                mouseScaleY = (double)240 / (double)this.window_height;
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
            NativeMethods.SetCursorPos(0, 0);
            try
            {
                nui.Initialize(RuntimeOptions.UseSkeletalTracking | RuntimeOptions.UseDepthAndPlayerIndex | RuntimeOptions.UseColor);
            }
            catch (Exception _Exception)
            {
                Console.WriteLine(_Exception.ToString());
                return false;
            }

            // for camera view
            nui.VideoStream.Open(ImageStreamType.Video, 2, ImageResolution.Resolution640x480, ImageType.Color);
            
            // for depth image
            nui.DepthStream.Open(ImageStreamType.Depth, 2, ImageResolution.Resolution320x240, ImageType.DepthAndPlayerIndex); 

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

        static Joint rightHand, leftHand, head;
        Boolean found_skeleton = false, found_depth = false;

        void nui_SkeletonFrameReady(Object sender, SkeletonFrameReadyEventArgs e)
        {
            SkeletonFrame allSkeletons = e.SkeletonFrame;

            SkeletonData skeleton = (from s in allSkeletons.Skeletons
                                     where s.TrackingState == SkeletonTrackingState.Tracked
                                     select s).FirstOrDefault();

            if (skeleton != null)
            {
                found_skeleton = true;
                // scale to the UI window size
                rightHand = scaleJoint(skeleton.Joints[JointID.HandRight]);
                leftHand = scaleJoint(skeleton.Joints[JointID.HandLeft]);
                head = scaleJoint(skeleton.Joints[JointID.Head]);
                // analyze skeleton
                if(found_depth) ui_detect_skeleton();
            }
            else found_skeleton = false;
        }

        private Joint scaleJoint(Joint joint)
        {
            // scale to the UI window size
            return joint.ScaleTo(this.window_width, this.window_height, .5f, .5f);
        }

        static Point convertPoint(Joint joint)
        {
            return new Point(Convert.ToInt32(joint.Position.X), Convert.ToInt32(joint.Position.Y));
        }

        void nui_monoFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            // 32-bit per pixel, RGBA image
            PlanarImage Image = e.ImageFrame.Image;
            video.Source = BitmapSource.Create(
                Image.Width, Image.Height, 96, 96,
                PixelFormats.Bgr32, null, Image.Bits, Image.Width * Image.BytesPerPixel);
        }

        void nui_DepthFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            if (!found_depth) found_depth = true;
            // show hands
            SetEllipsePosition(rightHandEllipse, 
                Canvas.GetLeft(depthVideo) + rightHand.Position.X * (double)160 / (double)this.window_width,
                Canvas.GetTop(depthVideo) + rightHand.Position.Y * (double)120 / (double)this.window_height);
            SetEllipsePosition(leftHandEllipse,
                Canvas.GetLeft(depthVideo) + leftHand.Position.X * (double)160 / (double)this.window_width,
                Canvas.GetTop(depthVideo) + leftHand.Position.Y * (double)120 / (double)this.window_height);
            // show depth
            ui_detection_depth(e.ImageFrame);
        }

        // *************************** for Kinect event actions *************************** //

        // process the joint information (currently right hand only)
        private void ui_detect_skeleton()
        {
            // detect PULL first
            // or HOLD
            if (!detectPullTwoHands() &&
                Math.Abs(head.Position.Y - rightHand.Position.Y) < 200
                && Math.Abs(head.Position.Y - leftHand.Position.Y) < 200)
            {
                if(displayMsg) this.textBlock_test.Text = "Hold";
                NativeMethods.SetCursorPos(0, 0);
                return;
            }
            // set the mouse position same as user's right hand deteced by Kinect
            NativeMethods.SetCursorPos(Convert.ToInt32(rightHand.Position.X), Convert.ToInt32(rightHand.Position.Y));

            // hand reach to the top (back area)
            if (rightHand.Position.Y <= 50)
            {
                try
                {
                    openPreviousLayer(active_menu, parent_menu_map[active_menu]);
                }
                catch (Exception e)
                {
                }
            }
            // detect SWIPE
            if (this.detectKinectSwipe) detectSwipe();

            // detect PUSH
            if (this.detectKinectPush) detectPush();
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
        const int click_time = 3; // hold an item for 2 seconds to select
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
            if (displayMsg) // for UI testing: show focus time
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
        
        private void detectSwipe()
        {
            Point currentHand = new Point(rightHand.Position.X, rightHand.Position.Y);
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
                    try
                    {
                        openPreviousLayer(active_menu, parent_menu_map[active_menu]);
                        if (displayMsg) this.textBlock_test.Text = "Swipe";
                    }
                    catch (Exception e)
                    {
                    }
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

        // ********************************* //
        //           push detection          //
        // ********************************* //

        long last_push_threshold_time = 0;
        double last_push_depth = 0;
        static int Threshold_push_detection = 50;

        private Boolean detectPush() {
            long timeNow = getCurrentTimestamp();
            if (detectKinectPush)
            {
                if (last_push_depth == 0)
                {
                    last_push_depth = rightHand.Position.Z;
                    last_push_threshold_time = timeNow;
                }
                else if (timeNow - last_push_threshold_time > Threshold_push_detection)
                {
                    if (last_push_depth - rightHand.Position.Z >= 0.4)
                    {
                        component_click(focus_component);
                        resetTimer();
                        if (this.displayMsg) this.textBlock_test.Text = "Push";
                        last_push_threshold_time = timeNow;
                        return true;
                    }
                }
            }
            return false;
        }

        // ********************************* //
        //           pull detection          //
        // ********************************* //

        long last_pull_threshold_time = 0;
        double last_pull_depth_right = 0, last_pull_depth_left = 0;
        static int Threshold_pull_detection = 20;

        private Boolean detectPullTwoHands()
        {
            long timeNow = getCurrentTimestamp();
            if (last_pull_depth_right == 0 || last_pull_depth_left == 0)
            {
                last_pull_depth_right = rightHand.Position.Z;
                last_pull_depth_left = leftHand.Position.Z;
                last_pull_threshold_time = timeNow;
            }
            else if (timeNow - last_pull_threshold_time > Threshold_pull_detection)
            {
                // this.textBlock_test.Text = (rightHand.Position.Z - last_pull_depth_right) + ", " + (leftHand.Position.Z - last_pull_depth_left);
                if (rightHand.Position.Z - last_pull_depth_right >= 0.2
                    && leftHand.Position.Z - last_pull_depth_left >= 0.2)
                {
                    if (this.displayMsg) this.textBlock_test.Text = "Pull with two hands";
                    last_pull_threshold_time = timeNow;
                    return true;
                }
            }
            return false;
        }

        // ********************************* //
        //           depth detection         //
        // ********************************* //

        private void ui_detection_depth(ImageFrame frame)
        {
            byte[] depthImage = depthMap(frame);
            // show the depth image
            depthVideo.Source = BitmapSource.Create(
                frame.Image.Width, frame.Image.Height, 96, 96, PixelFormats.Bgr32,
                null, depthImage, frame.Image.Width * PixelFormats.Bgr32.BitsPerPixel / 8);
        }

        private int GetDistanceWithPlayerIndex(byte firstFrame, byte secondFrame)
        {
            //offset by 3 in first byte to get value after player index 
            int distance = (int)(firstFrame >> 3 | secondFrame << 5);
            return distance;
        }

        private static int GetPlayerIndex(byte firstFrame)
        {
            //returns 0 = no player, 1 = 1st player, 2 = 2nd player...
            return (int)firstFrame & 7;
        }

        //equal coloring for monochromatic histogram
        const float MaxDepthDistance = 4000; // max value returned
        const float MinDepthDistance = 850; // min value returned
        const float MaxDepthDistanceOffset = MaxDepthDistance - MinDepthDistance;
        
        public static byte CalculateIntensityFromDepth(int distance)
        {
            //formula for calculating monochrome intensity for histogram
            return (byte)(255 - (255 * Math.Max(distance - MinDepthDistance, 0) / (MaxDepthDistanceOffset)));
        }

        private byte[] depthMap(ImageFrame imageFrame)
        {
            int height = imageFrame.Image.Height;
            int width = imageFrame.Image.Width;

            //Depth data for each pixel
            Byte[] depthData = imageFrame.Image.Bits;

            //monoFrame contains color information for all pixels in image
            //Height x Width x 4 (Red, Green, Blue, empty byte)
            Byte[] monoFrame = new byte[imageFrame.Image.Height * imageFrame.Image.Width *4];
            
            var depthIndex = 0;
            for (var y = 0; y < height; y++)
            {
                var heightOffset = y * width;

                for (var x = 0; x < width; x++)
                {
                    var index = (x + heightOffset) * 4;
                    var distance = GetDistanceWithPlayerIndex(depthData[depthIndex], depthData[depthIndex + 1]);

                    if (GetPlayerIndex(depthData[depthIndex]) > 0) // Show a player
                    {
                        //equal coloring for monochromatic histogram
                        var intensity = CalculateIntensityFromDepth(distance);
                        monoFrame[index + 0] = intensity; // BlueIndex
                        monoFrame[index + 1] = intensity; // GreenIndex
                        monoFrame[index + 2] = intensity; // RedIndex
                    }

                    //jump two bytes at a time
                    depthIndex += 2;
                }
            }
            return monoFrame;
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
            try
            {
                if (submenu_map.ContainsKey(component))
                { // Opens submenu
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
                else if (component == ButtonBack)
                { // back button
                    openPreviousLayer(active_menu, parent_menu_map[active_menu]);
                }
                else
                {
                    optionSelected(component);
                }
            }
            catch (Exception e)
            {
            }

            if(displayMsg)
                this.textBlock_test.Text = "Select";
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

            double zoomFactor = 2;
            Location selectedLoc;
            selectedLoc.Width = original_location[component].Width * zoomFactor;
            selectedLoc.Height = original_location[component].Height * zoomFactor;
            selectedLoc.Left = this.Width / 2 - selectedLoc.Width / 2;
            selectedLoc.Top = this.Height / 2 - selectedLoc.Height / 2;
            animateButton(component, selectedLoc, new Duration(TimeSpan.FromSeconds(0.5)),
                          delegate(object sender, EventArgs e)
                          {
                              SetLocation(component, selectedLoc);
                              animateButton(component, selectedLoc, new Duration(TimeSpan.FromSeconds(2.0)),
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
                    double zoomFactor = 1.5;
                    Location zoomedLoc = new Location(loc.Left - (loc.Width * (zoomFactor - 1.0) / 2), loc.Top - (loc.Height * (zoomFactor - 1.0) / 2),
                                                      loc.Width * zoomFactor, loc.Height * zoomFactor);
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

        // *************************** for testing *********************************** //

        private static void SetEllipsePosition(FrameworkElement ellipse, double X, double Y)
        {
            Canvas.SetLeft(ellipse, X);
            Canvas.SetTop(ellipse, Y);
        }

        private void checkBox_swipe_Click(object sender, RoutedEventArgs e)
        {
            if (checkBox_swipe.IsChecked == true)
            {
                this.detectKinectSwipe = true;
            }
            else
            {
                this.detectKinectSwipe = false;
            }
        }

        private void checkBox_push_Click(object sender, RoutedEventArgs e)
        {
            if (this.checkBox_push.IsChecked == true)
            {
                this.detectKinectPush = true;
            }
            else
            {
                this.detectKinectPush = false;
            }
        }

    } // end of class

    // for Kinect detection: time stamps
    public static class DateTimeExtensions
    {
        public static long GetTimestamp(this DateTime value)
        {
            return Convert.ToInt64(value.ToString("yyyyMMddHHmmssff"));
        }
    } // end of class
}
