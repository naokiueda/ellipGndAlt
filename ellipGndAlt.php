<?php

// written by Naoki Ueda, Locazing Inc.
// 
// Lincese:
// CC-BY 4.0  
// https://creativecommons.org/licenses/by/4.0/

//$start_time=microtime(true);
//ini_set('display_errors', '1');
 
$debugMode = 0;

function main(){
        
        global $debugMode;
        
        $geoidJband = 30.0/1800.0;
        $geoidIband = 0.025;

        //データベース接続情報を書いて下さい。
        $dbhost = "localhost";
        $dbdatabase = "******";
        $dbuser = "******";
        $dbpw = "******";
        
        
        //Get Parameter
        $z = $_GET['z'];
        $x = $_GET['x'];
        $y = $_GET['y'];
        $geoidonly = $_GET['geoidonly'];//ジオイドのみのタイルが欲しい時
        
        debug("z=".$z);
        debug("x=".$x);
        debug("y=".$y);
        
        //地理院のDEMタイル(PNG)をダウンロード
        if($geoidonly === "1"){
            $filepath = "./empty.png";    //ジオイドのみのタイルが欲しい時
        }else{
            $filepath = "http://cyberjapandata.gsi.go.jp/xyz/dem_png/".$z."/".$x."/".$y.".png";
        }
        $im = imageCreateFromPng($filepath);
        if($im===false){
            //タイルが無いとき
            http_response_code(404);
            echo "404 Object Not Found";
            die();        
        }
        
        //タイル四隅の緯度経度取得
        $lat0 = tile2lat(($y + 1) * 256, $z);
        $lon0 = tile2lon($x * 256, $z);
        $lat1 = tile2lat(($y) * 256, $z);
        $lon1 = tile2lon(($x + 1) * 256, $z);
        debug("lat0=".$lat0);
        debug("lat1=".$lat1);
        debug("lon0=".$lon0);
        debug("lon1=".$lon1);
        
        //タイル四隅のi/j座標取得
        //i/jは国土地理院のジオイドデータの格子点で、
        //iは東経120°から東へ向かって150°まで1.5分間隔で0～1800まで
        //jは北緯20°から北へ向かって50°まで1分間隔で0から1200まで
        $j0 = floor(($lon0 - 120 )/$geoidIband);
        $j1 = floor(($lon1 - 120 )/$geoidIband);
        $i0 = floor(($lat0 - 20 )/$geoidJband);
        $i1 = floor(($lat1 - 20 )/$geoidJband);
        debug("j0=".$j0);
        debug("j1=".$j1);
        debug("i0=".$i0);
        debug("i1=".$i1);
        
        //2重ループ内での計算をなるべく避けるため、必要な値を事前に計算しておく。XYそれぞれピクセル座標をキーにした配列で持っておく。
        //各経度成分ピクセルについて
        $px = 0;
        $j = $j0;
        debug("j=".$j);
        while($px < 256){
            $west[$px] = floor($j);        //使用するジオイドデータ西側格子点 j
            $east[$px] = $west[$px] + 1;   //使用するジオイドデータ東側格子点 j+1
            $u[$px] = $j - $west[$px];     //このピクセルと西側格子点までの距離（単位は格子点座標にて0～1の間）内挿計算方法の式で u に相当する
            $ui[$px] = 1-$u[$px];          //内挿計算方法の式で (1-u) に相当する
            $px+=1;
            $j = $j0 + (($j1 - $j0) * $px / 256);
        }

        $py = (int)0;
        $i = $i1;
        while($py < 256){
            $north[$py] = ceil($i);        //使用するジオイドデータ北側格子点 i+1
            $south[$py] = $north[$py] - 1; //使用するジオイドデータ南側格子点 i
            $t[$py] = $i - $south[$py];    //このピクセルと南側格子点までの距離（単位は格子点座標にて0～1の間）内挿計算方法の式で v に相当する
            $ti[$py] = 1- $t[$py];         //内挿計算方法の式で (1-v) に相当する
            $py++;
            $i = $i1 - (($i1 - $i0) * $py / 256) ;
        }
        
        
        //データベースアクセス
        //ジオイドが有効値（「e」でない）のデータのみデータベースに入っている
        $cn = mysql_connect($dbhost, $dbuser, $dbpw);
        if (!$cn) {
                debug('DB接続失敗');
                exit;
        }
        debug('DB接続成功');
        $db_selected = mysql_select_db($dbdatabase, $cn);
        if (!$db_selected){
                debug('データベース選択失敗');
                exit;
        }
        debug('データベース選択成功');
        
        mysql_set_charset('utf8');
        $sql = 'SELECT * FROM geoid WHERE j >= ' . $j0 . " AND j <= " . $j1 . " AND i >= " . $i0 . " AND i <= " . $i1 . " LIMIT 140000 " ;
        debug($sql);
        $result = mysql_query($sql);
        if (!$result) {
                debug('クエリー失敗');
                exit;
        }
        $exist = 0;
        while ($row = mysql_fetch_assoc($result)) {
            $exist = 1;
                $key = $row['i']."_".$row['j'];
                $GEOID[$key] = $row['geoid'];
                debug($row['geoid']);
        }
        
        if($exist===0){
            debug("NO Geoid DATA");
        }

        $close_flag = mysql_close($cn);
        if ($close_flag){
            debug('切断成功');
        }
        
        
        //ジオイド値計算
        $outbuf="";
        for($px=0;$px < 256; $px++){
            $linebuf="";
            for($py=0; $py<256; $py++){
                //計算
                if (!isset($GEOID[$south[$py]."_".$west[$px]]) 
                ||  !isset($GEOID[$south[$py]."_".$east[$px]]) 
                ||  !isset($GEOID[$north[$py]."_".$west[$px]]) 
                ||  !isset($GEOID[$north[$py]."_".$east[$px]]))
                {
                    //四隅の格子点のジオイド値のいずれかが「e」のとき、無効値にセット
                    //$linebuf .= "e,";
                    imagesetpixel($im, $px, $py, imagecolorallocate($im, 128, 0, 0));
                    //echo "e";
                    continue;          
                }else{
                    if($geoidonly === "1"){
                        //ジオイドのみのタイルが欲しい時
                        $dem = 0;
                    }else{
                        //タイルピクセルからDEM値を取得
                        $rgb = imagecolorat($im, $px, $py);
                        $r = ($rgb >> 16) & 0xFF;
                        $g = ($rgb >> 8) & 0xFF;
                        $b = $rgb & 0xFF;
                        if($r === 128 && $g === 0 && $b ===0 ){
                            //DEMが無効値なら無効値のままにしておく？
                            continue;          
                            //DEMが無効値なのは水面が大半なので、ジオイド値をセットしたい時はDEM値ゼロを与える
                            //$dem = 0.0;
                        }else{
                            $x = 65536 * $r + 256 * $g + $b;
                            //echo( "  " . $x . "    ");
                            if($x == 8388608){
                                //無効値
                                imagesetpixel($im, $px, $py, imagecolorallocate($im, 128, 0, 0));//e
                                continue;
                            }else if($x < 8388608){
                                $dem = 0.01 * $x;
                            }else{ //if($x > 8388608)
                                $dem = 0.01 * ($x - 16777216);
                            }
                        }
                    }
                    
                    //事前計算した値からジオイド値を計算
                    $geoid =                                                                 //Z=
                         $ti[$py] * $ui[$px] * $GEOID[$south[$py]."_".$west[$px]]         //  (1-t)*(1-u)*Z(i,j)
                       + $ti[$py] * $u[$px] * $GEOID[$south[$py]."_".$east[$px]]          // +(1-t)*u    *Z(i,j+1)
                       + $t[$py] * $ui[$px] * $GEOID[$north[$py]."_".$west[$px]]          // +    t*(1-u)*Z(i+1,j)
                       + $t[$py] * $u[$px] * $GEOID[$north[$py]."_".$east[$px]]           // +    t*u    *Z(i+1,j+1)
                       ;
                    //地上高（楕円体高）の計算 
                    $gndAltElla = $dem + $geoid;        //楕円体高 ＝ DEM値 ＋ ジオイド値
                    
                    //標高PNGのRGBに再変換
                    if($gndAltElla >= 0){
                        $x = $gndAltElla * 100;
                    }else{
                        $x = $gndAltElla * 100 + 16777216;
                    }
                    $x = $x + 0.5;//0.001mの桁で四捨五入
                    $r = floor($x / 65536);
                    $x = $x - $r * 65536;
                    $g = floor($x / 256);
                    $b = $x - $g * 256;
                    //ピクセル値を楕円体高で上書き
                    imagesetpixel($im, $px, $py, imagecolorallocate($im, $r, $g, $b));
                }
            }
        }
        //出力
        header('Content-Type: image/png');
        imagepng($im);
        imagedestroy($im);
}
 

function tile2lon($x, $z) {
        return (($x/(pow(2,$z)*256))*360-180);
}
function tile2lat($y, $z)
{
        $n=pi()-2*pi()* $y/(pow(2,$z)*256);
        return (180/pi() * atan(0.5*(exp($n)-exp(-$n))));
}
 
 
function debug($str){
        global $debugMode;
        if($debugMode===1){
                print($str);
                print("<br/>");
        }else if($debugMode===2){
                var_dump($str);
                print("<br/>");
        }
}
 
 
main();
        
$end_time=microtime(true);
$processedtime=$end_time - $start_time;
//echo "<br/>time：".sprintf('%0.5f',$processedtime)."<br/>";
?>
