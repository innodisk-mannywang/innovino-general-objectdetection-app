using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
    public partial class MainWindow : Window
    {

        //Strcture, API of InnoVINO.dll 
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct OMZ_Model
        {
            public String lpXML;
            public String lpBIN;
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
        private static extern int IVINO_Init(ref IntPtr dwServiceId, ref OMZ_Model config);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int IVINO_Inference(IntPtr dwServiceId, ref ImageData pData, ref ObjectDatas pOutput, bool bAsync);
        [DllImport("InnoVINO.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int IVINO_Uninit(IntPtr dwServiceId);


        //app
        private List<string> model_name = new List<string>();
        private List<string> model_path = new List<string>();
        private List<string> device_name = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void Cb_omz_models_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void Slider_threadshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }        
    }
}
