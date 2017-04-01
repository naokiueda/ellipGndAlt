//ellipGndAlt in C#
// 
// written by Naoki Ueda, Locazing Inc.
// Lincese:
// CC-BY 4.0  
// https://creativecommons.org/licenses/by/4.0/

using System;
using System.Text;
using System.IO;
using System.Net;
using System.Drawing;



namespace geoidtilelib
{
    /// <summary>
    /// ジオイド高のPNGイメージ、（ジオイド高＋標高DEM値）のPNGイメージを作成するクラス
    /// 
    /// 使い方
    /// 
    /// 最初に一度初期化すること
    /// ellipGndAlt.init(@"c:\....\gsigeo2011_ver2.asc");
    ///
    /// ジオイドのみのタイルを取得する場合
    /// Bitmap tile = ellipGndAlt.getGeoidTile(z, x, y, true);
    /// 
    /// ジオイド＋DEM値のタイルを取得する場合（DEM値は都度、国土地理院の標高タイルをダウンロードします）
    /// Bitmap tile = ellipGndAlt.getGeoidTile(z, x, y, true);
    /// 
    /// </summary>
    public class ellipGndAlt
    {
        /// <summary>
        /// ジオイドデータをあらかじめ読み込んでおく配列。staticなので一度セットすれば常に利用できる
        /// </summary>
        private static double[,] geoid;

        /// <summary>
        /// コンストラクタ
        /// ジオイドデータ「gsigeo2011_ver2.asc」をあらかじめStatic配列に読込んでおく。
        /// </summary>
        /// <param name="geoidDataPath"></param>
        public static bool init(string Path_to_gsigeo2011_ver2_asc)
        {
            if (!File.Exists(Path_to_gsigeo2011_ver2_asc))
            {
                //ジオイドデータファイルが無い
                return false;
            }
            //国土地理院標高タイルをダウンロードするときの最大接続数を上げるにはこれをセット。デフォルトは2
            int l = System.Net.ServicePointManager.DefaultConnectionLimit;
            System.Net.ServicePointManager.DefaultConnectionLimit = 64;

            StreamReader sr = new StreamReader(Path_to_gsigeo2011_ver2_asc, Encoding.GetEncoding("Shift_JIS"));
            if (sr == null)
            {
                //ジオイドデータファイルが開けない
                return false;
            }
            //ヘッダを読み飛ばす
            sr.ReadLine();

            //ジオイドデータをあらかじめ1801x1021のStatic配列に読み込んでおく
            geoid = new double[1801, 1201];
            int la = 0, lo = 0;
            while (sr.Peek() > -1)
            {
                string[] g = sr.ReadLine().Trim().Split(' ');
                for (int i = 0; i < g.Length; i++)
                {
                    if (g[i].Trim() == "")
                    {
                        continue;
                    }
                    geoid[la, lo] = double.Parse(g[i]);
                    lo++;
                    if (lo == 1201)
                    {
                        lo = 0;
                        la++;
                    }
                }
            }
            sr.Close();

            return true;
        }

        /// <summary>
        /// ジオイド値の関連するタイルイメージを作成する
        /// １．国土地理院の標高PNGタイルをダウンロードする。（ジオイド値のみの場合は空のタイルイメージを準備）
        /// ２．タイル各ピクセルの座標に相当するジオイド値を算出する
        /// ３．ジオイド値＋標高値を算出、標高PNG形式にエンコードしピクセルに上書きセットする
        /// </summary>
        /// <param name="z">ズームレベル</param>
        /// <param name="x">タイル番号X</param>
        /// <param name="y">タイル番号Y</param>
        /// <param name="geoidonly">true：ジオイド値のみのタイルを生成、false:ジオイド値＋標高値（＝地表面の楕円体高）のタイルを作成</param>
        /// <returns></returns>
        public static Bitmap getGeoidTile(int z, int x, int y, bool geoidonly)
        {
            Bitmap bitmap;
            if (geoidonly)
            {
                bitmap = new Bitmap(256, 256);
                Graphics g = Graphics.FromImage(bitmap);
                g.FillRectangle(new SolidBrush(Color.FromArgb(0, 0, 0)), g.VisibleClipBounds);//値ゼロで初期化
            }
            else
            {
                try
                {
                    var wc = new System.Net.WebClient();
                    var stream = wc.OpenRead("http://cyberjapandata.gsi.go.jp/xyz/dem_png/" + z + "/" + x + "/" + y + ".png");
                    bitmap = new Bitmap(stream);
                    wc.Dispose();
                    stream.Close();
                }
                catch (WebException we)
                {
                    if (we.Status == WebExceptionStatus.ProtocolError && we.Message.IndexOf("404") >= 0)
                    {
                        //タイルがない
                        bitmap = new Bitmap(256, 256);
                        Graphics g = Graphics.FromImage(bitmap);
                        g.FillRectangle(new SolidBrush(Color.FromArgb(128, 0, 0)), g.VisibleClipBounds);//無効タイル
                        return bitmap;
                    }
                    else
                    {
                        bitmap = new Bitmap(256, 256);
                        Graphics g = Graphics.FromImage(bitmap);
                        g.FillRectangle(new SolidBrush(Color.FromArgb(128, 0, 0)), g.VisibleClipBounds); //無効タイル
                        return bitmap;
                    }
                }
            }
            //ここまでで標高PNGをダウンロードしたか、ゼロ値の標高PNGが準備できている。
            //各ピクセルにジオイド値を加算する
            //ここでは分かりやすくGetPixcel/SetPixcelを使っていますが、これは処理が遅いので、必要であればメモリに直接アクセスする方法等に差替えてください。
            double lat0 = tile2lat((y + 1) * 256, z);
            double lon0 = tile2lon(x * 256, z);
            double lat1 = tile2lat(y * 256, z);
            double lon1 = tile2lon((x + 1) * 256, z);
            for (int py = 0; py < bitmap.Height; py++)
            {
                for (int px = 0; px < bitmap.Width; px++)
                {
                    Color color = bitmap.GetPixel(px, py);
                    double dem = 0;
                    double X;
                    if (color.R == 128 && color.B == 0 && color.G == 0)
                    {
                        //DEMが無効値なら無効値のままにしておく
                        continue;
                    }
                    else
                    {
                        X = 65536 * (double)(color.R) + 256 * (double)(color.G) + (double)(color.B);

                        if (X == 8388608)
                        {
                            //無効値
                            bitmap.SetPixel(px, py, Color.FromArgb(128, 0, 0));//e
                            continue;
                        }
                        else if (X < 8388608)
                        {
                            dem = 0.01 * X;
                        }
                        else
                        { //if(X > 8388608)
                            dem = 0.01 * (X - 16777216);
                        }
                    }
                    double lon = lon0 + (lon1 - lon0) / 256 * px;
                    double lat = lat1 - (lat1 - lat0) / 256 * py;
                    double geoid = getGeoid(lat, lon);
                    if (geoid == 999)
                    {
                        bitmap.SetPixel(px, py, Color.FromArgb(128,0,0));
                        continue;
                    }
                    double gndAltElla = dem + geoid;

                    //標高PNGにエンコード

                    if(gndAltElla >= 0){
                        X = gndAltElla * 100;
                    }else{
                        X = gndAltElla * 100 + 16777216;
                    }
                    X = X + 0.5;//0.001mの桁で四捨五入
                    double r = Math.Floor (X / 65536);
                    X = X - r * 65536;
                    double gr = Math.Floor(X / 256);
                    double b = X - gr * 256;

                    bitmap.SetPixel(px, py, Color.FromArgb((int)r, (int)gr, (int)b));
                }
            }
            return bitmap;

        }
        /// <summary>
        /// 任意の緯度経度のジオイド値を取得する
        /// 計算方法は国土地理院「asc取扱説明書」のジオイド高内挿計算方法を利用
        /// </summary>
        /// <param name="lat">緯度</param>
        /// <param name="lon">経度</param>
        /// <returns></returns>
        private static double getGeoid(double lat, double lon)
        {
            //囲う矩形を求める
            if (lat < 20 || lat > 50 || lon < 120 || lon > 150)
            {
                return 999;//無効値
            }
            int j = (int)Math.Floor((lon - 120) / 0.025000);
            int i = (int)Math.Floor((lat - 20) / (30.0 / 1800.0));
            if ((geoid[i, j] == 999) || (geoid[i, j + 1] == 999) || (geoid[i + 1, j] == 999) || (geoid[i + 1, j + 1] == 999))
            {
                return 999;//無効値
            }
            double wlon = 120 + j * 0.025000;
            double elon = 120 + (j + 1) * 0.025000;
            double slat = 20 + i * (30.0 / 1800.0);
            double nlat = 20 + (i + 1) * (30.0 / 1800.0);

            double t = (lat - slat) / (nlat - slat);
            double u = (lon - wlon) / (elon - wlon);

            double Z = (1 - t) * (1 - u) * geoid[i, j]
                     + (1 - t) * u * geoid[i, j + 1]
                     + t * (1 - u) * geoid[i + 1, j]
                     + t * u * geoid[i + 1, j + 1];
            Z *= 10000;
            Z = Math.Floor(Z + 0.5);
            Z /= 10000;
            return Z;
        }
        /// <summary>
        /// ズームレベルとタイルX座標から経度を取得
        /// </summary>
        /// <param name="x">タイルX座標</param>
        /// <param name="z">ズームレベル</param>
        /// <returns></returns>
        private static double tile2lon(double x, double z)
        {
            return ((x / (Math.Pow(2, z) * 256)) * 360 - 180);
        }
        /// <summary>
        /// ズームレベルとタイルY座標から緯度を取得
        /// </summary>
        /// <param name="y">タイルY座標</param>
        /// <param name="z">ズームレベル</param>
        /// <returns></returns>
        private static double tile2lat(double y, double z)
        {
            double n = Math.PI - 2 * Math.PI * y / (Math.Pow(2, z) * 256);
            return (180 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n))));
        }


 
    }
}
