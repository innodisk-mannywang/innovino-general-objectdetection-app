using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace innovino_general_objectdetection_app
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        //InnoVINO.dll

        enum RESULT
        {
            OK = 0,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct OMZ_Model
        {
            public string lpXML;
            public string lpBIN;
            public string lpDevice;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet =CharSet.Ansi)]
        public struct Device
        {
            public Int32 type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            public string name;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct AvailableDevices
        {
            public int nCount;
            public IntPtr pDevices;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ImageData
        {
            public UInt16 uiWidth;
            public UInt16 uiHeight;
            public UInt32 uiSize;
            public IntPtr pData;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ObjectData
        {
            public float conf;
            public Int32 label;
            public UInt16 x_min;
            public UInt16 y_min;
            public UInt16 x_max;
            public UInt16 y_max;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ObjectDatas
        {
            public int nCount;
            public IntPtr pObjects;
        }

        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern RESULT IVINO_Init(ref IntPtr dwServiceId);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern RESULT IVINO_GetAvailableDevices(IntPtr dwServiceId, ref AvailableDevices devices);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int IVINO_AddEngine(IntPtr dwServiceId, ref OMZ_Model model);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int IVINO_Inference(IntPtr dwServiceId, ref ImageData pData, ref ObjectDatas pOutput, bool bAsync);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern RESULT IVINO_Uninit(IntPtr dwServiceId);


        //win32 api
        [DllImport("kernel32.dll")]
        private static extern void OutputDebugString(string lpOutputString);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);


        //app
        private List<string> model_name = new List<string>();
        private List<string> model_path = new List<string>();
        private List<string> device_name = new List<string>();
        private List<ObjectData> last_result = new List<ObjectData>();
        private IntPtr serviceid = IntPtr.Zero;
        private string last_image = "";
        private VideoCapture objVideoCapture = new VideoCapture();
        private BackgroundWorker bk_online_source = new BackgroundWorker();
        private bool m_bStopIdentify = false;
        private double infer_conf_threshold = 0.0f;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            if (IVINO_Init(ref serviceid) != RESULT.OK)
            {
                MessageBox.Show("IVINO_Init failed.");
                this.Close();
            }

            init_device_combobox();

            init_model_combobox();

            OMZ_Model model = new OMZ_Model();
            model.lpBIN = model_path[cb_omz_models.SelectedIndex] + ".bin";
            model.lpXML = model_path[cb_omz_models.SelectedIndex] + ".xml";
            model.lpDevice = device_name[cb_device.SelectedIndex];

            if(IVINO_AddEngine(serviceid, ref model) < 0)
            {
                MessageBox.Show("IVINO_AddEngine failed.");
            }

            bk_online_source.DoWork += Bk_online_source_DoWork;
            bk_online_source.RunWorkerAsync();
        }

        private void Bk_online_source_DoWork(object sender, DoWorkEventArgs e)
        {
            if (!objVideoCapture.IsOpened())
            {
                objVideoCapture.Open(0);
            }

            while (!m_bStopIdentify)
            {
                try
                {
                    using (Mat frame = new Mat())
                    {
                        if (objVideoCapture.Read(frame))
                        {
                            //show the frame to image
                            Application.Current.Dispatcher.Invoke(new Action(() =>
                            {                                
                                Cv2.Resize(frame, frame, new OpenCvSharp.Size(rawimage.Width, rawimage.Height));
                                rawimage.Source = BitmapToBitmapSource(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame));                                
                            }));

                            //Do Inference in innovino-sdk
                            //Cv2.CvtColor(image, image, ColorConversionCodes.RGB2BGR);
                            ImageData data = new ImageData();
                            data.uiWidth = (UInt16)frame.Cols;
                            data.uiHeight = (UInt16)frame.Rows;
                            data.uiSize = (UInt32)(frame.Cols * frame.Rows * frame.Channels());
                            data.pData = frame.Data;

                            ObjectDatas output = new ObjectDatas();
                            int nResult = IVINO_Inference(serviceid, ref data, ref output, false);
                            if (nResult > 0)
                            {
                                Application.Current.Dispatcher.Invoke(new Action(() =>
                                {
                                    canvas.Children.Clear();
                                }));

                                last_result.Clear();

                                for (int n = 0; n < output.nCount; n++)
                                {
                                    ObjectData obj = Marshal.PtrToStructure<ObjectData>(output.pObjects + Marshal.SizeOf(typeof(ObjectData)) * n);
                                    last_result.Add(obj);
                                }

                                foreach (ObjectData obj in last_result)
                                {
                                    if (obj.label == 0)
                                    {
                                        continue;
                                    }

                                    if (obj.conf < infer_conf_threshold)
                                    {
                                        continue;
                                    }

                                    //int weight = 2;
                                    int x = Math.Min(obj.x_min, obj.x_max);
                                    int y = Math.Min(obj.y_min, obj.y_max);
                                    int width = Math.Max(obj.x_min, obj.x_max) - x;
                                    int height = Math.Max(obj.y_min, obj.y_max) - y;
                                    //Cv2.Rectangle(image, new OpenCvSharp.Rect(x, y, width, height), Scalar.Red, 3);
                                    //OpenCvSharp.Rect rt;
                                    //rt.X = x - weight;
                                    //rt.Y = y - weight;
                                    //rt.Width = width + weight;
                                    //rt.Height = height + weight;

                                    Application.Current.Dispatcher.Invoke(new Action(() =>
                                    {
                                        System.Windows.Shapes.Rectangle rect;
                                        rect = new System.Windows.Shapes.Rectangle();
                                        rect.Stroke = new SolidColorBrush(Colors.Red);
                                        rect.StrokeThickness = 2;
                                        rect.Width = width;
                                        rect.Height = height;
                                        Canvas.SetLeft(rect, x);
                                        Canvas.SetTop(rect, y);
                                        canvas.Children.Add(rect);
                                    }));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OutputDebugString("Bk_online_source_DoWork Exception : " + ex.Message);
                }
            }
        }

        private void init_device_combobox()
        {
            AvailableDevices devices = new AvailableDevices();
            if (IVINO_GetAvailableDevices(serviceid, ref devices) != RESULT.OK)
            {
                MessageBox.Show("IVINO_GetAvailableDevices failed.");
                this.Close();
            }

            //OutputDebugString("deivce.ncount : " + devices.nCount);

            for (int i = 0; i < devices.nCount; i++)
            {
                Device device = Marshal.PtrToStructure<Device>(devices.pDevices + Marshal.SizeOf(typeof(Device)) * i);
                device_name.Add(device.name);
            }

            foreach (string device in device_name)
                cb_device.Items.Add(device);

            cb_device.SelectedIndex = 0;
        }

        private void init_model_combobox()
        {
            model_name.Add("face-detection-0102");
            model_path.Add(Directory.GetCurrentDirectory().Substring(0, Directory.GetCurrentDirectory().LastIndexOf(@"\")) + "\\Pretrain-models\\intel\\face-detection-0102\\FP16\\face-detection-0102");

            model_name.Add("person-attributes-recognition-crossroad-0230");
            model_path.Add(Directory.GetCurrentDirectory().Substring(0, Directory.GetCurrentDirectory().LastIndexOf(@"\")) + "\\Pretrain-models\\intel\\person-attributes-recognition-crossroad-0230\\FP16\\person-attributes-recognition-crossroad-0230");

            foreach (string model in model_name)
            {
                cb_omz_models.Items.Add(model);
            }
            
            cb_omz_models.SelectedIndex = 0;
        }

        private void Cb_omz_models_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (serviceid != IntPtr.Zero)
                {
                    OMZ_Model model = new OMZ_Model();
                    model.lpBIN = model_path[cb_omz_models.SelectedIndex] + ".bin";
                    model.lpXML = model_path[cb_omz_models.SelectedIndex] + ".xml";
                    model.lpDevice = device_name[cb_device.SelectedIndex];

                    if (IVINO_AddEngine(serviceid, ref model) < 0)
                    {
                        MessageBox.Show("IVINO_AddEngine failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                OutputDebugString(ex.Message);
            }
        }

        private void Slider_threadshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                Slider obj = (Slider)sender;
                if(obj == null)
                {
                    OutputDebugString("obj is null");
                }
                else
                {
                    infer_conf_threshold = Math.Round(obj.Value, 1) / 10.0f;
                    tb_threshold.Text = infer_conf_threshold.ToString();
                }                
            }
            catch (Exception ex)
            {
                OutputDebugString(ex.Message);
            }
            
            //_show_bbox(last_result);
        }

        private void Btn_start_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Btn_stop_Click(object sender, RoutedEventArgs e)
        {

        }

        private ObjectDatas Do_Inference(Mat frame)
        {
            //Do Inference in innovino-sdk
            //Cv2.CvtColor(image, image, ColorConversionCodes.RGB2BGR);
            ImageData data = new ImageData();
            data.uiWidth = (UInt16)frame.Cols;
            data.uiHeight = (UInt16)frame.Rows;
            data.uiSize = (UInt32)(frame.Cols * frame.Rows * frame.Channels());
            data.pData = frame.Data;

            ObjectDatas output = new ObjectDatas();            
            int nResult = IVINO_Inference(serviceid, ref data, ref output, false);
            return output;
            //if (nResult > 0)
            //{
            //    //Application.Current.Dispatcher.Invoke(new Action(() =>
            //    //{
            //    //    canvas.Children.Clear();
            //    //}));

            //    last_result.Clear();

            //    for (int n = 0; n < output.nCount; n++)
            //    {
            //        ObjectData obj = Marshal.PtrToStructure<ObjectData>(output.pObjects + Marshal.SizeOf(typeof(ObjectData)) * n);
            //        last_result.Add(obj);
            //    }

            //    foreach (ObjectData obj in last_result)
            //    {
            //        if (obj.label == 0)
            //        {
            //            continue;
            //        }

            //        if (obj.conf < infer_conf_threshold)
            //        {
            //            continue;
            //        }

            //        //int weight = 2;
            //        int x = Math.Min(obj.x_min, obj.x_max);
            //        int y = Math.Min(obj.y_min, obj.y_max);
            //        int width = Math.Max(obj.x_min, obj.x_max) - x;
            //        int height = Math.Max(obj.y_min, obj.y_max) - y;
            //        //Cv2.Rectangle(image, new OpenCvSharp.Rect(x, y, width, height), Scalar.Red, 3);
            //        //OpenCvSharp.Rect rt;
            //        //rt.X = x - weight;
            //        //rt.Y = y - weight;
            //        //rt.Width = width + weight;
            //        //rt.Height = height + weight;

            //        Application.Current.Dispatcher.Invoke(new Action(() =>
            //        {
            //            DrawBBox();
            //        }));
            //    }
            //}
        }

        private void DrawBBox(int x, int y, int width, int height)
        {
            System.Windows.Shapes.Rectangle rect;
            rect = new System.Windows.Shapes.Rectangle();
            rect.Stroke = new SolidColorBrush(Colors.Red);
            rect.StrokeThickness = 2.5;
            rect.Width = width;
            rect.Height = height;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
        }

        
        private BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            IntPtr ptr = bitmap.GetHbitmap();
            BitmapSource result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(ptr, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(ptr);
            return result;
        }

        private void Rbtn_offline_Click(object sender, RoutedEventArgs e)
        {            
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                last_image = openFileDialog.FileName;

            if (File.Exists(last_image))
            {
                //check the camera status. Closed it if it opened.
                if (objVideoCapture.IsOpened())
                {
                    m_bStopIdentify = true;
                    objVideoCapture.Release();
                }
                
                Mat frame = Cv2.ImRead(last_image, ImreadModes.Color);                
                rawimage.Source = BitmapToBitmapSource(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame));
                
                //ImageData data = new ImageData();
                //data.uiWidth = (UInt16)frame.Cols;
                //data.uiHeight = (UInt16)frame.Rows;
                //data.uiSize = (UInt32)(frame.Cols * frame.Rows * frame.Channels());
                //data.pData = frame.Data;

                //ObjectDatas output = new ObjectDatas();
                //int nResult = IVINO_Inference(serviceid, ref data, ref output, false);
                //if (nResult > 0)
                //{
                //    OutputDebugString("canvas.chiledren.count : " + canvas.Children.Count);                    
                //    canvas.Children.Clear();
                //    OutputDebugString("canvas.chiledren.count : " + canvas.Children.Count);
                //    canvas.UpdateLayout();
                //    canvas.Visibility = Visibility.Collapsed;
                //    canvas.Visibility = Visibility.Visible;
                    

                //    last_result.Clear();

                //    for (int n = 0; n < output.nCount; n++)
                //    {
                //        ObjectData obj = Marshal.PtrToStructure<ObjectData>(output.pObjects + Marshal.SizeOf(typeof(ObjectData)) * n);
                //        last_result.Add(obj);
                //    }

                //    //foreach (ObjectData obj in last_result)
                //    //{
                //    //    if (obj.label == 0)
                //    //    {
                //    //        continue;
                //    //    }

                //    //    if (obj.conf < infer_conf_threshold)
                //    //    {
                //    //        continue;
                //    //    }

                //    //    //int weight = 2;
                //    //    int x = Math.Min(obj.x_min, obj.x_max);
                //    //    int y = Math.Min(obj.y_min, obj.y_max);
                //    //    int width = Math.Max(obj.x_min, obj.x_max) - x;
                //    //    int height = Math.Max(obj.y_min, obj.y_max) - y;
                        
                //    //    System.Windows.Shapes.Rectangle rect;
                //    //    rect = new System.Windows.Shapes.Rectangle();
                //    //    rect.Stroke = new SolidColorBrush(Colors.Red);
                //    //    rect.StrokeThickness = 2;
                //    //    rect.Width = width;
                //    //    rect.Height = height;
                //    //    Canvas.SetLeft(rect, x);
                //    //    Canvas.SetTop(rect, y);
                //    //    canvas.Children.Add(rect);
                //    //    canvas.UpdateLayout();
                //    //}

                //    this.UpdateLayout();
                //}
            }                
        }

        private void Rbtn_online_Click(object sender, RoutedEventArgs e)
        {
            m_bStopIdentify = false;
            bk_online_source.RunWorkerAsync();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (serviceid != IntPtr.Zero)
                IVINO_Uninit(serviceid);

            m_bStopIdentify = true;

            if (objVideoCapture.IsOpened())
            {                
                objVideoCapture.Release();
            }
        }

        private void Cb_device_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (serviceid != IntPtr.Zero)
                {
                    OMZ_Model model = new OMZ_Model();
                    model.lpBIN = model_path[cb_omz_models.SelectedIndex] + ".bin";
                    model.lpXML = model_path[cb_omz_models.SelectedIndex] + ".xml";
                    model.lpDevice = device_name[cb_device.SelectedIndex];

                    if (IVINO_AddEngine(serviceid, ref model) < 0)
                    {
                        MessageBox.Show("IVINO_AddEngine failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                OutputDebugString(ex.Message);
            }
        }
    }
}
