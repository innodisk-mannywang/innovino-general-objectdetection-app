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
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        private static extern int IVINO_AddFace(IntPtr dwServiceId, ref ImageData pData, string label);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int IVINO_FaceRecogEx(IntPtr dwServiceId, ref ImageData pData, ref ObjectData pOutput);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern float IVINO_FaceRecog(IntPtr dwServiceId, ref ImageData pData1, ref ImageData pData2, bool bAsync);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern RESULT IVINO_Uninit(IntPtr dwServiceId);


        //win32 api
        [DllImport("kernel32.dll")]
        private static extern void OutputDebugString(string lpOutputString);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();


        //app
        private List<string> model_name = new List<string>();
        private List<string> model_path = new List<string>();
        private List<string> device_name = new List<string>();
        private List<ObjectData> last_result = new List<ObjectData>();
        private IntPtr serviceid = IntPtr.Zero;
        private string last_image = "";
        private VideoCapture objVideoCapture = new VideoCapture();
        private BackgroundWorker bk_online_source = new BackgroundWorker();
        private BackgroundWorker bk_offline_source = new BackgroundWorker();
        private bool m_bStopIdentify = false;
        private double infer_conf_threshold = 0.0f;
        private bool bLock = false;
        private double fRatioX = 0.0;
        private double fRatioY = 0.0;
        private uint start = 0, end = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //Intial IVINO libray
            if (IVINO_Init(ref serviceid) != RESULT.OK)
            {
                MessageBox.Show("IVINO_Init failed.");
                this.Close();
            }

            //Get All available devices and add to combobox
            init_device_combobox();

            //Prepare all pre-train models and add to combobox
            init_model_combobox();

            //Initial online loop.
            bk_online_source.DoWork += Bk_online_source_DoWork;
            bk_online_source.WorkerSupportsCancellation = true;

            //Initial offline flow.
            bk_offline_source.DoWork += Bk_offline_source_DoWork;

            load_face_image();
        }

        private void load_face_image()
        {
            string path = Directory.GetCurrentDirectory().Substring(0, Directory.GetCurrentDirectory().LastIndexOf(@"\")) + "\\InnoFaces_OnlyFace\\IPA";
            string[] files = Directory.GetFiles(path);
            
            foreach (string file in files)
            {
                //Mat frame1 = Cv2.ImRead(file, ImreadModes.Color);
                //ImageData data1 = new ImageData();
                //data1.uiWidth = (UInt16)frame1.Cols;
                //data1.uiHeight = (UInt16)frame1.Rows;
                //data1.uiSize = (UInt32)(frame1.Cols * frame1.Rows * frame1.Channels());
                //data1.pData = frame1.Data;
                //OutputDebugString(file);

                //string path2 = Directory.GetCurrentDirectory().Substring(0, Directory.GetCurrentDirectory().LastIndexOf(@"\")) + "\\InnoFaces_OnlyFace\\IPA2";
                //string[] tmpfiles = Directory.GetFiles(path2);
                //foreach (string tmpfile in tmpfiles)
                //{
                //    Mat frame2 = Cv2.ImRead(tmpfile, ImreadModes.Color);
                //    ImageData data2 = new ImageData();
                //    data2.uiWidth = (UInt16)frame2.Cols;
                //    data2.uiHeight = (UInt16)frame2.Rows;
                //    data2.uiSize = (UInt32)(frame2.Cols * frame2.Rows * frame2.Channels());
                //    data2.pData = frame2.Data;

                //    OutputDebugString(tmpfile);

                //    float result = IVINO_FaceRecog(serviceid, ref data1, ref data2, true);
                //    OutputDebugString("Conf : " + result);
                //}

                Mat frame = Cv2.ImRead(file, ImreadModes.Color);

                //fRatioX = rawimage.ActualWidth / frame.Cols;
                //fRatioY = rawimage.ActualHeight / frame.Rows;

                ImageData data = new ImageData();
                data.uiWidth = (UInt16)frame.Cols;
                data.uiHeight = (UInt16)frame.Rows;
                data.uiSize = (UInt32)(frame.Cols * frame.Rows * frame.Channels());
                data.pData = frame.Data;
                OutputDebugString(file);
                IVINO_AddFace(serviceid, ref data, file.Substring(file.LastIndexOf(@"\") + 1, 4));
            }
        }

        private void Bk_offline_source_DoWork(object sender, DoWorkEventArgs e)
        {
            //throw new NotImplementedException();

            OutputDebugString("offline " + last_image);
            while (bLock)
            {
                m_bStopIdentify = true;
                OutputDebugString("bLock!!!");
            }

            while (!bk_online_source.CancellationPending)
            {
                bk_online_source.CancelAsync();
                OutputDebugString("bk_online_source.CancellationPending!!!");
            }

            start = GetTickCount();

            Mat frame = Cv2.ImRead(last_image, ImreadModes.Color);
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                rawimage.Source = BitmapToBitmapSource(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame));
            }));

            fRatioX = rawimage.ActualWidth / frame.Cols;
            fRatioY = rawimage.ActualHeight / frame.Rows;

            ImageData data = new ImageData();
            data.uiWidth = (UInt16)frame.Cols;
            data.uiHeight = (UInt16)frame.Rows;
            data.uiSize = (UInt32)(frame.Cols * frame.Rows * frame.Channels());
            data.pData = frame.Data;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                for (int i = 0; i < canvas.Children.Count;)
                {
                    canvas.Children.RemoveAt(0);
                }

                tb_inference_result.Text = "";
            }));

            last_result.Clear();

            ObjectDatas output = new ObjectDatas();
            int nResult = IVINO_Inference(serviceid, ref data, ref output, false);            
            if (nResult > 0)
            {
                end = GetTickCount();

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

                    int x = Math.Min(obj.x_min, obj.x_max);
                    int y = Math.Min(obj.y_min, obj.y_max);
                    int width = Math.Max(obj.x_min, obj.x_max) - x;
                    int height = Math.Max(obj.y_min, obj.y_max) - y;

                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        System.Windows.Shapes.Rectangle rect;
                        rect = new System.Windows.Shapes.Rectangle();
                        rect.Stroke = new SolidColorBrush(Colors.Red);
                        rect.StrokeThickness = 2;
                        rect.Width = width * fRatioX;
                        rect.Height = height * fRatioY;
                        Canvas.SetLeft(rect, x * fRatioX);
                        Canvas.SetTop(rect, y * fRatioY);
                        canvas.Children.Add(rect);

                        string logmsg = "Off(" + (end - start) + "ms)" + "Label:" + obj.label + ",BBox(" + obj.x_min + "," + obj.y_min + "," + obj.x_max + "," + obj.y_max + ")";
                        tb_inference_result.AppendText(logmsg + Environment.NewLine);
                        tb_inference_result.AppendText("rawimage width : " + frame.Cols + " height : " + frame.Rows + Environment.NewLine);
                        tb_inference_result.ScrollToEnd();
                    }));
                }
            }
        }

        private void Bk_online_source_DoWork(object sender, DoWorkEventArgs e)
        {
            start = GetTickCount();
            bool bTimeout = false;
            while (!bTimeout && !objVideoCapture.IsOpened())
            {
                if (GetTickCount() - start < 5000)
                    objVideoCapture.Open(0);
                else
                    bTimeout = true;
            }

            while (!m_bStopIdentify)
            {
                try
                {
                    if(!bLock)
                    {
                        bLock = true;
                        using (Mat frame = new Mat())
                        {
                                start = GetTickCount();
                                if (objVideoCapture.Read(frame))
                                {
                                    //show the frame to image
                                    Application.Current.Dispatcher.Invoke(new Action(() =>
                                    {
                                        Cv2.Resize(frame, frame, new OpenCvSharp.Size(rawimage.Width, rawimage.Height));
                                        rawimage.Source = BitmapToBitmapSource(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame));
                                    }));

                                    //Do Inference in innovino-sdk
                                    ImageData data = new ImageData();
                                    data.uiWidth = (UInt16)frame.Cols;
                                    data.uiHeight = (UInt16)frame.Rows;
                                    data.uiSize = (UInt32)(frame.Cols * frame.Rows * frame.Channels());
                                    data.pData = frame.Data;

                                    fRatioX = rawimage.ActualWidth / frame.Cols;
                                    fRatioY = rawimage.ActualHeight / frame.Rows;

                                    ObjectDatas output = new ObjectDatas();
                                    int nResult = IVINO_Inference(serviceid, ref data, ref output, false);
                                    if (nResult > 0)
                                    {
                                        end = GetTickCount();

                                        Application.Current.Dispatcher.Invoke(new Action(() =>
                                        {
                                            for (int i = 0; i < canvas.Children.Count;)
                                            {
                                                canvas.Children.RemoveAt(0);
                                            }
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

                                            int x = Math.Min(obj.x_min, obj.x_max);
                                            int y = Math.Min(obj.y_min, obj.y_max);
                                            int width = Math.Max(obj.x_min, obj.x_max) - x;
                                            int height = Math.Max(obj.y_min, obj.y_max) - y;

                                            Mat m_roi = new Mat(frame, new OpenCvSharp.Rect(x, y, width, height));
                                            Mat roi = new Mat();
                                            m_roi.CopyTo(roi);
                                            ImageData roidata = new ImageData();
                                            roidata.uiWidth = (UInt16)roi.Cols;
                                            roidata.uiHeight = (UInt16)roi.Rows;
                                            roidata.uiSize = (UInt32)(roi.Cols * roi.Rows * roi.Channels());
                                            roidata.pData = roi.Data;
                                            ObjectData roioutput = new ObjectData();

                                            Application.Current.Dispatcher.Invoke(new Action(() =>
                                            {                                                
                                                IVINO_FaceRecogEx(serviceid, ref roidata, ref roioutput);

                                                System.Windows.Shapes.Rectangle rect;
                                                rect = new System.Windows.Shapes.Rectangle();
                                                rect.Stroke = new SolidColorBrush(Colors.Red);
                                                rect.StrokeThickness = 2;
                                                rect.Width = width * fRatioX;
                                                rect.Height = height * fRatioY;
                                                Canvas.SetLeft(rect, x * fRatioX);
                                                Canvas.SetTop(rect, y * fRatioY);
                                                canvas.Children.Add(rect);
                                                Label frid = new Label();
                                                frid.FontSize = 20;
                                                frid.Foreground = Brushes.Red;
                                                frid.Content = roioutput.label;
                                                Canvas.SetLeft(frid, x * fRatioX);
                                                Canvas.SetTop(frid, y * fRatioY);
                                                canvas.Children.Add(frid);
                                                string logmsg = "On(" + (end - start) + "ms)" + "Label:" + roioutput.label + " Confidence:" + obj.conf + ",BBox(" + obj.x_min + "," + obj.y_min + "," + obj.x_max + "," + obj.y_max + ")";
                                                tb_inference_result.AppendText(logmsg + Environment.NewLine);
                                                tb_inference_result.ScrollToEnd();
                                            }));
                                        }
                                    }
                                }
                            }
                        }
                }
                catch (Exception ex)
                {
                    OutputDebugString("Bk_online_source_DoWork Exception : " + ex.Message);
                }
                finally{
                    bLock = false;
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

                    _redraw_bbox(last_result);
                }                
            }
            catch (Exception ex)
            {
                OutputDebugString(ex.Message);
            }
        }

        private void Btn_start_Click(object sender, RoutedEventArgs e)
        {

            try
            {
                OMZ_Model model = new OMZ_Model();
                model.lpBIN = model_path[cb_omz_models.SelectedIndex] + ".bin";
                model.lpXML = model_path[cb_omz_models.SelectedIndex] + ".xml";
                model.lpDevice = device_name[cb_device.SelectedIndex];

                if (IVINO_AddEngine(serviceid, ref model) < 0)
                {
                    MessageBox.Show("IVINO_AddEngine failed.");
                }

                //update controllers on UI
                Confidence.Visibility = Visibility.Visible;
                Source.Visibility = Visibility.Visible;
                btn_stop.IsEnabled = true;
                btn_start.IsEnabled = false;
                cb_device.IsEnabled = false;
                cb_omz_models.IsEnabled = false;

                m_bStopIdentify = false;
                bk_online_source.RunWorkerAsync();
            }
            catch(Exception ex)
            {
                OutputDebugString("[Btn_start_Click] Exception : " + ex.Message);
            }
        }

        private void Btn_stop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (objVideoCapture.IsOpened())
                {
                    m_bStopIdentify = true;
                    objVideoCapture.Release();
                }

                Confidence.Visibility = Visibility.Collapsed;
                Source.Visibility = Visibility.Collapsed;
                btn_stop.IsEnabled = false;
                btn_start.IsEnabled = true;
                cb_device.IsEnabled = true;
                cb_omz_models.IsEnabled = true;
            }
            catch (Exception ex)
            {
                OutputDebugString("[Btn_stop_Click] Exception : " + ex.Message);
            }            
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
                m_bStopIdentify = true;
                bk_online_source.CancelAsync();
                bk_offline_source.RunWorkerAsync();
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

        private void _redraw_bbox(List<ObjectData> objs)
        {
            for (int i = 0; i < canvas.Children.Count;)
            {
                canvas.Children.RemoveAt(0);
            }

            foreach (ObjectData obj in objs)
            {
                if (obj.label == 0)
                {
                    continue;
                }

                if (obj.conf < infer_conf_threshold)
                {
                    continue;
                }

                int x = Math.Min(obj.x_min, obj.x_max);
                int y = Math.Min(obj.y_min, obj.y_max);
                int width = Math.Max(obj.x_min, obj.x_max) - x;
                int height = Math.Max(obj.y_min, obj.y_max) - y;

                System.Windows.Shapes.Rectangle rect;
                rect = new System.Windows.Shapes.Rectangle();
                rect.Stroke = new SolidColorBrush(Colors.Red);
                rect.StrokeThickness = 2;
                rect.Width = width * fRatioX;
                rect.Height = height * fRatioY;
                Canvas.SetLeft(rect, x * fRatioX);
                Canvas.SetTop(rect, y * fRatioY);
                canvas.Children.Add(rect);

                string logmsg = "On(" + (end - start) + "ms)" + "Label:" + obj.label + "Confidence:" + obj.conf + ",BBox(" + obj.x_min + "," + obj.y_min + "," + obj.x_max + "," + obj.y_max + ")";
                tb_inference_result.AppendText(logmsg + Environment.NewLine);
                tb_inference_result.ScrollToEnd();
            }
        }
    }
}
