// Kinect skeletal tracking
// for CS260 hw2 with Derrick
// Author: peggychi
// Latest Update: 09/17/2011

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ** for Kinect component **
        Runtime nui = new Runtime();
        int window_height = 0, window_width = 0;

        // for UI testing
        Button[] main_button_list;
        Button[] game_button_list;

        public MainWindow()
        {
            InitializeComponent();
            // get window height and width
            this.window_height = Convert.ToInt32(this.Height);
            this.window_width = Convert.ToInt32(this.Width);
            // set mouse cursor icon as hand
            this.Cursor = Cursors.Hand;
            
            // for UI testing
            main_button_list = new Button[] { mainButton_Apps, mainButton_Games, mainButton_Movies, mainButton_Music, mainButton_Settings};
            game_button_list = new Button[] { button_game_item0, button_game_item1, button_game_item2, button_game_item3 };
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

        DispatcherTimer timer;
        int focus_time = 0; // user holds an item
        const int click_time = 2; // hold an item for 2 seconds to select
        Button focus_component = null;

        // process the joint information (currently right hand only)
        private void ui_detection(Joint joint)
        {
            // scale to the UI window size
            joint = joint.ScaleTo(this.window_width, this.window_height, .5f, .5f);
            // currently sets the mouse position same as user's right hand deteced by Kinect
            NativeMethods.SetCursorPos(Convert.ToInt32(joint.Position.X), Convert.ToInt32(joint.Position.Y));
        }

        private partial class NativeMethods
        {
            [System.Runtime.InteropServices.DllImportAttribute("user32.dll", EntryPoint = "SetCursorPos")]
            [return: System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool SetCursorPos(int X, int Y);
        }

        // timer for hold and select
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

        // *************************** navigation interactions *************************** //

        private void component_click(Button component)
        {
            if (main_button_list.Contains(component)) { // main list
                openNextLayer(main_button_list, game_button_list);
            }
            else if (game_button_list.Contains(component)) { // sublist
            }
            else { // back button
                openPreviousLayer(game_button_list, main_button_list);
            }

            // for UI testing
            this.textBlock_test.Text = "";
        }

        private void openNextLayer(Button[] currentList, Button[] nextList)
        {
            // disable previous layer and show next layer
            for (int i = 0; i < currentList.Length | i < nextList.Length; i++)
            {
                if(i<currentList.Length) currentList[i].IsEnabled = false;
                if (i < nextList.Length) nextList[i].Visibility = System.Windows.Visibility.Visible;
            }
            // show back area
            this.button_back.Visibility = System.Windows.Visibility.Visible;
        }

        private void openPreviousLayer(Button[] currentList, Button[] previousList)
        {
            // hide current layer and show previous layer
            for (int i = 0; i < currentList.Length | i < previousList.Length; i++)
            {
                if (i < currentList.Length) currentList[i].Visibility = System.Windows.Visibility.Hidden;
                if (i < previousList.Length) previousList[i].IsEnabled = true;
            }
            // hiden back area
            this.button_back.Visibility = System.Windows.Visibility.Hidden;
        }

        private void component_enter(Button component)
        {
            if (focus_component == null || focus_component.Name != component.Name)
            {
                focus_component = component;
                restartTimer();
            }
        }

        private void component_leave(Button component)
        {
            resetTimer();
            focus_component = null;
        }

        // *************************** mouse interactions *************************** //

        private void mainButton_Games_Click(object sender, RoutedEventArgs e)
        {

            component_click(this.mainButton_Games);
        }

        private void mainButton_Games_MouseEnter(object sender, MouseEventArgs e)
        {
            component_enter(this.mainButton_Games);
        }

        private void mainButton_Games_MouseLeave(object sender, MouseEventArgs e)
        {
            component_leave(this.mainButton_Games);
        }

        private void button_back_Click(object sender, RoutedEventArgs e)
        {
            openPreviousLayer(this.game_button_list, this.main_button_list);
        }

        private void button_back_MouseEnter(object sender, MouseEventArgs e)
        {
            component_enter(this.button_back);
        }

        private void button_back_MouseLeave(object sender, MouseEventArgs e)
        {
            component_leave(this.button_back);
        }
    }
}
