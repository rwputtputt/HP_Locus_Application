using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScottPlot;
using System.IO.Ports;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Xml.Schema;
using System.Data.SqlTypes;
using System.Threading;
using System.Windows.Forms.VisualStyles;
using ScottPlot.Plottable;

namespace TestWinformProject
{
    public partial class Form1 : Form
    {
        private double MHSL = 340;
        private double most_recent_mhsl = 340;
        private const double MEAN_SEA_LEVEL_HEIGHT = 6371000; //in m
        private const double METERS_TO_YARDS = 1.09361;
        private const int MAX_DATA_SET_SIZE = 2000;

        private double[] origin_point = new double[2] { 37.9484478, -91.7702610 };
        private double[] direction_vector = new double[2] { 0.89442720371049, -0.447213570078808 };
        private double[] current_location = new double[2];

        private double[] current_data = new double[MAX_DATA_SET_SIZE];
        private int[] series_locations = new int[30];
        private int num_series;
        private int num_data_points;

        private DirectoryInfo direc = new DirectoryInfo(Path.GetFullPath(System.AppDomain.CurrentDomain.BaseDirectory));
        private string TempFilePath = Path.Combine(Path.GetFullPath(System.AppDomain.CurrentDomain.BaseDirectory), "Streaming.txt");
        private string CurrFilePath = Path.Combine(Path.GetFullPath(System.AppDomain.CurrentDomain.BaseDirectory), "Streaming.txt");
        private string OriginFilePath = Path.Combine(Path.GetFullPath(System.AppDomain.CurrentDomain.BaseDirectory), "Origin.txt");
        private bool OriginFileBeingUsed = false;

        private ScottPlot.Plottable.MarkerPlot HighlightedPoint;
        private ScottPlot.Plottable.SignalPlot[] MySignalPlot;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            comboBox1_update();
            comboBox2_update();

            if (!File.Exists(TempFilePath))
            {
                StreamWriter w = new StreamWriter(TempFilePath, true);
                w.Close();
            }
            update_origin_data();
        }

        private void comboBox1_update()
        {
            comboBox1.Items.Clear();
            comboBox1.Items.Add("Disconnected");
            comboBox1.SelectedIndex = 0;
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                comboBox1.Items.Add(port);
            }
        }

        private void comboBox2_update()
        {
            comboBox2.Items.Clear();

            FileInfo[] data_files = direc.GetFiles("*.txt");
            foreach (FileInfo data_file in data_files)
            {
                if (data_file.Name != "Origin.txt")
                {
                    comboBox2.Items.Add(data_file.Name);
                }
            }
            comboBox2.Text = "Streaming.txt";
        }

        private void SerialPort1_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            char directive;
            byte[] lat_bytes = new byte[4];
            byte[] lon_bytes = new byte[4];
            byte[] mhsl_bytes = new byte[4];
            int lat;
            int lon;
            double height;
            StreamWriter w = new StreamWriter(TempFilePath, true);

            while (serialPort1.BytesToRead >= 9)
            {
                directive = (char)serialPort1.ReadByte();
                //Console.Write("Directive: ");
                //Console.WriteLine(directive);
                if ((directive == 'C') || (directive == 'N') || (directive == 'D'))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        lat_bytes[i] = (byte)serialPort1.ReadByte();
                    }
                    lat = BitConverter.ToInt32(lat_bytes, 0);
                    //Console.WriteLine(lat);

                    for (int i = 0; i < 4; i++)
                    {
                        lon_bytes[i] = (byte)serialPort1.ReadByte();
                    }
                    lon = BitConverter.ToInt32(lon_bytes, 0);
                    //Console.WriteLine(lon);

                    for (int i = 0; i < 4; i++)
                    {
                        mhsl_bytes[i] = (byte)serialPort1.ReadByte();
                    }
                    height = BitConverter.ToInt32(mhsl_bytes, 0) / 1000.0;
                    most_recent_mhsl = height;
                    //Console.WriteLine(lon);

                    if (directive == 'N')
                    {
                        w.WriteLine();
                        series_locations[num_series] = num_data_points;
                        num_series++;
                    }
                    current_location[0] = (double)lat / 10000000.0;
                    current_location[1] = (double)lon / 10000000.0;
                    Console.WriteLine("Current location: " + current_location[0] + ", " + current_location[1]);
                    if (directive != 'D')
                    {
                        w.Write('S');
                        w.Write(current_location[0]);
                        w.Write(',');
                        w.Write(current_location[1]);
                        w.Write(",");

                        double norm_lat = ((current_location[0] - origin_point[0]) * direction_vector[0]) + origin_point[0];
                        double norm_lon = ((current_location[1] - origin_point[1]) * direction_vector[1]) + origin_point[1];

                        current_data[num_data_points] = get_Distance(origin_point[0], origin_point[1], norm_lat, norm_lon, MEAN_SEA_LEVEL_HEIGHT);// + MHSL);
                        num_data_points++;
                    }
                    textBox5.Text = current_location[0].ToString() +" lat, " + current_location[1].ToString() + " lon";
                }
            }
            w.Close();

            load_distance_data_points_from_file(CurrFilePath);
            update_distance_plot(formsPlot3);
        }

        private void formsPlot3_Load(object sender, EventArgs e)
        {
            formsPlot3.Plot.XLabel("Time");
            formsPlot3.Plot.YLabel("Yards");
            formsPlot3.Plot.Title("Distance On-Field");
            formsPlot3.Plot.Legend();
            load_distance_data_points_from_file(CurrFilePath);
            update_distance_plot(formsPlot3);
            formsPlot3.Refresh();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Console.WriteLine("////////////dumping data!////////////");
            string[] file_lines = File.ReadAllLines(CurrFilePath);
            foreach (string line in file_lines)
            {
                int data_in_line = (line.Split('S')).Length - 1;
                int count = 0;
                double[] lat_vals = new double[data_in_line];
                int lat_count = 0;
                double[] lon_vals = new double[data_in_line];
                int lon_count = 0;
                while (lat_count < data_in_line)
                {
                    string temp = "";
                    while (line[++count] != ',')
                    {
                        temp += line[count];
                    }
                    lat_vals[lat_count++] = double.Parse(temp);

                    temp = "";
                    while (line[++count] != ',')
                    {
                        temp += line[count];
                    }
                    count++;
                    lon_vals[lon_count++] = double.Parse(temp);
                }
                Console.WriteLine(lat_vals.Length + " lats and " + lon_vals.Length + " lons on file");
                Console.Write("lats: ");
                for (int i = 0; i < lat_vals.Length; i++)
                {
                    if (i % 10 == 0)
                    {
                        Console.WriteLine();
                    }
                    Console.Write(lat_vals[i] + " ");
                }
                Console.WriteLine();
                Console.Write("lons: ");
                for (int i = 0; i < lon_vals.Length; i++)
                {
                    if (i % 10 == 0)
                    {
                        Console.WriteLine();
                    }
                    Console.Write(lon_vals[i] + " ");
                }
                Console.WriteLine();
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            StreamWriter w = new StreamWriter(CurrFilePath, true);
            w.WriteLine();
            w.Close();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            File.Delete(CurrFilePath);
            comboBox2_update();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox1.Text != "Disconnected")
            {
                try
                {
                    serialPort1.DataReceived += new SerialDataReceivedEventHandler(SerialPort1_DataReceived);
                    serialPort1.PortName = comboBox1.Text;
                    serialPort1.Open();
                    serialPort1.ReadExisting();
                }
                catch
                {
                    Console.WriteLine("Serial Port did not open.");
                }
            }
            else
            {
                try
                {
                    serialPort1.Close();
                }
                catch
                {
                    Console.WriteLine("Serial Port could not close.");
                }
            }
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            CurrFilePath = Path.Combine(Path.GetFullPath(System.AppDomain.CurrentDomain.BaseDirectory), comboBox2.Text);
            textBox3.Text = comboBox2.Text;
            //update_plot(formsPlot1, CurrFilePath);
            //update_location_plot(formsPlot2, CurrFilePath);
            load_distance_data_points_from_file(CurrFilePath);
            update_distance_plot(formsPlot3);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!textBox3.Text.EndsWith(".txt"))
            {
                textBox3.Text += ".txt";
            }

            string file_path = Path.Combine(Path.GetFullPath(System.AppDomain.CurrentDomain.BaseDirectory), textBox3.Text);
            if (File.Exists(file_path))
            {
                File.Delete(file_path);
            }
            StreamWriter w = new StreamWriter(file_path, true);
            string[] file_lines = File.ReadAllLines(TempFilePath);
            foreach (string line in file_lines)
            {
                w.WriteLine(line);
            }
            w.Close();

            comboBox2_update();
        }

        void update_plot(FormsPlot plot, string file)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return;
                }

                plot.Plot.Clear();
                string[] file_lines = File.ReadAllLines(file);
                foreach (string line in file_lines)
                {
                    int data_in_line = (line.Split('S')).Length - 1;
                    int count = 0;
                    double[] lat_vals = new double[data_in_line];
                    int lat_count = 0;
                    double[] lon_vals = new double[data_in_line];
                    int lon_count = 0;
                    while (lat_count < data_in_line)
                    {
                        string temp = "";
                        while (line[++count] != ',')
                        {
                            temp += line[count];
                        }
                        lat_vals[lat_count++] = double.Parse(temp);

                        temp = "";
                        while (line[++count] != ',')
                        {
                            temp += line[count];
                        }
                        count++;
                        lon_vals[lon_count++] = double.Parse(temp);
                    }
                    if (lat_vals.Length > 0)
                    {
                        textBox5.Text = lat_vals[lat_vals.Length - 1] + " lat, " + lon_vals[lon_vals.Length - 1] + " lon";
                        plot.Plot.AddScatter(lat_vals, lon_vals);
                    }
                }
                plot.Refresh();
            }
            catch (Exception e)
            {
                Console.WriteLine("Formsplots failed to refresh");
                Console.WriteLine(e.ToString());
            }
        }

        private void update_location_plot(FormsPlot plot, string file)
        {
            if (!File.Exists(file))
            {
                return;
            }

            plot.Plot.Clear();
            string[] file_lines = File.ReadAllLines(file);
            foreach (string line in file_lines)
            {
                int data_in_line = (line.Split('S')).Length - 1;
                if (data_in_line == 0)
                {
                    continue;
                }
                int count = 0;
                double[] lat_vals = new double[data_in_line];
                int lat_count = 0;
                double[] lon_vals = new double[data_in_line];
                int lon_count = 0;
                while (lat_count < data_in_line)
                {
                    string temp = "";
                    while (line[++count] != ',')
                    {
                        temp += line[count];
                    }
                    lat_vals[lat_count++] = double.Parse(temp);

                    temp = "";
                    while (line[++count] != ',')
                    {
                        temp += line[count];
                    }
                    count++;
                    lon_vals[lon_count++] = double.Parse(temp);
                }

                double[] normalized_lats_x = new double[data_in_line];
                double[] normalized_lons_x = new double[data_in_line];
                double[] normalized_lats_y = new double[data_in_line];
                double[] normalized_lons_y = new double[data_in_line];
                for (int i = 0; i < normalized_lats_x.Length; i++)
                {
                    normalized_lats_x[i] = ((lat_vals[i] - origin_point[0]) * direction_vector[0]) + origin_point[0];
                    normalized_lons_x[i] = ((lon_vals[i] - origin_point[1]) * direction_vector[1]) + origin_point[1];

                    normalized_lats_y[i] = ((lat_vals[i] - origin_point[0]) * direction_vector[1]) + origin_point[0];
                    normalized_lons_y[i] = ((lon_vals[i] - origin_point[1]) * direction_vector[0]) + origin_point[1];
                }

                double[] distances_x = new double[data_in_line];
                double[] distances_y = new double[data_in_line];
                for (int i = 0; i < distances_x.Length; i++)
                {
                    distances_y[i] = get_Distance(origin_point[0], origin_point[1], normalized_lats_x[i], normalized_lons_x[i], MEAN_SEA_LEVEL_HEIGHT);
                    distances_x[i] = -1 * get_Distance(origin_point[0], origin_point[1], normalized_lats_y[i], normalized_lons_y[i], MEAN_SEA_LEVEL_HEIGHT);
                }

                plot.Plot.AddScatter(distances_x, distances_y);
            }
            plot.Refresh();
        }

        private void update_distance_plot(FormsPlot plot)
        {
            //Console.WriteLine("start!");
            if (num_series == 0)
            {
                plot.Plot.Clear();
                plot.Refresh();
                return;
            }


            plot.Plot.Clear();
            MySignalPlot = new ScottPlot.Plottable.SignalPlot[num_series];
            int series_legend_num = 1;
            for (int i = 0; i < num_series; i++)
            {
                //Console.WriteLine("Series Location: " + series_locations[i]);
                if (series_locations[i] > num_data_points)
                {
                    //Console.WriteLine("Breaking! series location: " + series_locations[i]);
                    //Console.WriteLine("num_data_points: " + num_data_points);
                    break;
                }
                int size_of_series;
                if (i == 0)
                {
                    size_of_series = series_locations[i];
                    double[] temp_data_series = new double[size_of_series];
                    for (int j = 0; j < temp_data_series.Length; j++)
                    {
                        temp_data_series[j] = current_data[j];
                    }
                    //plot.Plot.AddSignal(temp_data_series, 10, label: "Series " + (series_legend_num));
                    ///*
                    MySignalPlot[series_legend_num - 1] = plot.Plot.AddSignal(temp_data_series, 10, label: "Series " + (series_legend_num));
                    var marker = plot.Plot.AddMarker(((float)temp_data_series.Length - 1) / 10, temp_data_series[temp_data_series.Length - 1]);
                    marker.Text = temp_data_series[temp_data_series.Length - 1].ToString("#.##");
                    marker.Color = MySignalPlot[series_legend_num - 1].LineColor;
                    marker.TextFont.Color = marker.Color;
                    marker.TextFont.Alignment = Alignment.UpperCenter;
                    //*/
                }
                else
                {
                    size_of_series = series_locations[i] - series_locations[i - 1];
                    double[] temp_data_series = new double[size_of_series];
                    for (int j = 0; j < temp_data_series.Length; j++)
                    {
                        temp_data_series[j] = current_data[j + series_locations[i - 1]];
                    }
                    //plot.Plot.AddSignal(temp_data_series, 10, label: "Series " + (series_legend_num));
                    ///*
                    MySignalPlot[series_legend_num - 1] = plot.Plot.AddSignal(temp_data_series, 10, label: "Series " + (series_legend_num));
                    var marker = plot.Plot.AddMarker(((float)temp_data_series.Length - 1) / 10, temp_data_series[temp_data_series.Length - 1]);
                    marker.Text = temp_data_series[temp_data_series.Length - 1].ToString("#.##");
                    marker.Color = MySignalPlot[series_legend_num - 1].LineColor;
                    marker.TextFont.Color = marker.Color;
                    marker.TextFont.Alignment = Alignment.UpperCenter;
                    //*/
                }
                series_legend_num++;
            }
            HighlightedPoint = formsPlot3.Plot.AddPoint(0, 0);
            HighlightedPoint.Color = Color.Red;
            HighlightedPoint.MarkerSize = 10;
            HighlightedPoint.MarkerShape = ScottPlot.MarkerShape.openCircle;
            HighlightedPoint.IsVisible = false;

            plot.Refresh();
            //Console.WriteLine("should refresh!");
        }

        private void load_distance_data_points_from_file(string file)
        {
            if (!File.Exists(file))
            {
                return;
            }

            string[] file_lines = File.ReadAllLines(file);
            num_data_points = 0;
            num_series = 0;
            foreach (string line in file_lines)
            {
                int data_in_line = (line.Split('S')).Length - 1;
                if (data_in_line == 0)
                {
                    continue;
                }
                if (data_in_line + num_data_points > MAX_DATA_SET_SIZE)
                {
                    Console.WriteLine("Error loading file! more than " + MAX_DATA_SET_SIZE + " data points!");
                    return;
                }
                int count = 0;
                double[] lat_vals = new double[data_in_line];
                int lat_count = 0;
                double[] lon_vals = new double[data_in_line];
                int lon_count = 0;
                while (lat_count < data_in_line)
                {
                    string temp = "";
                    while (line[++count] != ',')
                    {
                        temp += line[count];
                    }
                    lat_vals[lat_count++] = double.Parse(temp);

                    temp = "";
                    while (line[++count] != ',')
                    {
                        temp += line[count];
                    }
                    count++;
                    lon_vals[lon_count++] = double.Parse(temp);
                }

                double[] norm_lats = new double[data_in_line];
                double[] norm_lons = new double[data_in_line];
                for (int i = 0; i < norm_lats.Length; i++)
                {
                    norm_lats[i] = ((lat_vals[i] - origin_point[0]) * direction_vector[0]) + origin_point[0];
                    norm_lons[i] = ((lon_vals[i] - origin_point[1]) * direction_vector[1]) + origin_point[1];
                }

                for (int i = 0; i < data_in_line; i++)
                {
                    current_data[i + num_data_points] = get_Distance(origin_point[0], origin_point[1], norm_lats[i], norm_lons[i], MEAN_SEA_LEVEL_HEIGHT);// + MHSL);
                }

                num_data_points += data_in_line;
                series_locations[num_series++] = num_data_points;
            }
        }

        private double get_Distance(double lat1, double lon1, double lat2, double lon2, double height) // in yards
        {
            double phi_1 = lat1 * Math.PI / 180;
            double phi_2 = lat2 * Math.PI / 180;
            double d_phi = (lat2 - lat1) * Math.PI / 180;
            double d_lambda = (lon2 - lon1) * Math.PI / 180;

            double a = Math.Sin(d_phi / 2) * Math.Sin(d_phi / 2)
                + Math.Cos(phi_1) * Math.Cos(phi_2) * Math.Sin(d_lambda / 2) * Math.Sin(d_lambda / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = height * c;
            distance *= METERS_TO_YARDS;
            return distance;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            origin_point[0] = current_location[0];
            origin_point[1] = current_location[1];
            MHSL = most_recent_mhsl;
            update_origin_file();
        }

        private void button7_Click(object sender, EventArgs e)
        {
            direction_vector[0] = origin_point[0] - current_location[0];
            direction_vector[1] = origin_point[1] - current_location[1];
            double normalize = Math.Sqrt(direction_vector[0] * direction_vector[0] + direction_vector[1] * direction_vector[1]);
            direction_vector[0] /= normalize;
            direction_vector[1] /= normalize;
            update_origin_file();

        }

        private void update_origin_file()
        {
            try
            {
                if (File.Exists(OriginFilePath))
                {
                    File.Delete(OriginFilePath);

                }
                StreamWriter w = new StreamWriter(OriginFilePath, true);
                w.Write(origin_point[0]);
                w.Write(",");
                w.Write(origin_point[1]);
                w.Write(",");
                w.Write(direction_vector[0]);
                w.Write(",");
                w.Write(direction_vector[1]);
                w.Write(",");
                w.Write(MHSL);
                w.Close();
                Console.WriteLine("Set origin at: " + origin_point[0] + ", " + origin_point[1] + ", " + direction_vector[0] + ", " + direction_vector[1] + ", " + MHSL);
            }
            catch (Exception e)
            {
                Console.WriteLine("Origin file failed to update.");
            }
        }

        private void update_origin_data()
        {
            try
            {
                if (!File.Exists(OriginFilePath))
                {
                    return;
                }
                string[] data_lines = File.ReadAllLines(OriginFilePath);
                string temp = "";
                int count = -1;
                while (data_lines[0][++count] != ',')
                {
                    temp += data_lines[0][count];
                }
                origin_point[0] = double.Parse(temp);

                temp = "";
                while (data_lines[0][++count] != ',')
                {
                    temp += data_lines[0][count];
                }
                origin_point[1] = double.Parse(temp);

                temp = "";
                while (data_lines[0][++count] != ',')
                {
                    temp += data_lines[0][count];
                }
                direction_vector[0] = double.Parse(temp);

                temp = "";
                while (data_lines[0][++count] != ',')
                {
                    temp += data_lines[0][count];
                }
                direction_vector[1] = double.Parse(temp);

                temp = "";
                while ((++count) < data_lines[0].Length - 1)
                {
                    temp += data_lines[0][count];
                }
                MHSL = double.Parse(temp);
                Console.WriteLine("Set origin at: " + origin_point[0] + ", " + origin_point[1] + ", " + direction_vector[0] + ", " + direction_vector[1] + ", " + MHSL);
            }
            catch (Exception e)
            {
                Console.WriteLine("Origin data failed to update");
                Console.WriteLine(e.ToString());
            }
        }

        private void formsPlot3_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // determine point nearest the cursor
                (double mouseCoordX, double mouseCoordY) = formsPlot3.GetMouseCoordinates();
                //Console.WriteLine("mouse coord Y: " + mouseCoordY);
                double[] X_points = new double[num_series], Y_points = new double[num_series];
                int[] points_Index = new int[num_series];
                for (int i = 0; i < num_series; i++)
                {
                    (X_points[i], Y_points[i], points_Index[i]) = MySignalPlot[i].GetPointNearestX(mouseCoordX);
                }
                int best_index = 0;
                double closest_y = mouseCoordY - Y_points[0];
                for (int i = 0; i < num_series; i++)
                {
                    double y_diff = mouseCoordY - Y_points[i];
                    if (y_diff < 0)
                    {
                        y_diff *= -1;
                    }
                    if (y_diff < closest_y)
                    {
                        best_index = i;
                        closest_y = y_diff;
                    }
                }
                double[] y = MySignalPlot[best_index].Ys;
                double farthest_yardage = y[0];
                foreach (double point in y)
                {
                    if (point > farthest_yardage)
                    {
                        farthest_yardage = point;
                    }
                }
                //(farthest_yardage, _) = MySignalPlot[best_index].GetYDataRange(0, MySignalPlot[best_index].Ys.Length);
                //Console.WriteLine("best y coordinate: " + Y_points[best_index]);
                //Console.WriteLine("mouse Coordinate: " + mouseCoordY);


                // place the highlight over the point of interest
                HighlightedPoint.X = X_points[best_index];
                HighlightedPoint.Y = Y_points[best_index];
                HighlightedPoint.IsVisible = true;

                formsPlot3.Render();

                // update the GUI to describe the highlighted point
                textBox6.Text = Y_points[best_index].ToString("#.##") + " yards at " + X_points[best_index].ToString("#.##") + " seconds";
                textBox9.Text = farthest_yardage.ToString("#.##") + " yards";
            }
            catch
            {
                Console.WriteLine("Failed to render mouse_over graph");
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            File.Delete(CurrFilePath);
            StreamWriter w = new StreamWriter(CurrFilePath, true);
            w.Close();
            load_distance_data_points_from_file(CurrFilePath);
            update_distance_plot(formsPlot3);
        }
    }
}