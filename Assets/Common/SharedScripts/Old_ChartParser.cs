
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CQMusicGame.Shared
{
    public static class Old_ChartParser
{
    /// <summary>
    /// 将NoteInfo列表转为字符串
    /// </summary>
    /// <param name="notesInfos"></param>
    /// <returns></returns>
    public static string ListToString(List<NoteData> notesInfos)
    {
        List<NoteData> orderedList = notesInfos.OrderBy(x => x.TargetTime).ToList();
        
        string chart = "";
        foreach (NoteData noteData in orderedList)
        {
            chart += noteData.TargetTime.ToString(CultureInfo.InvariantCulture);
            chart += ":";
            chart += noteData.LaneIndex.ToString(CultureInfo.InvariantCulture);
            chart += ",";
            switch (noteData.Type)
            {
                case NoteType.Dot:
                    chart += "t";
                    break;
                case NoteType.Dash:
                    chart += "h";
                    break;
                case NoteType.Mute:
                    chart += "d";
                    break;
            }
            switch (noteData.Type)
            {
                case NoteType.Dash:
                case NoteType.Mute:
                    chart += ",";
                    chart += noteData.SustainDuration.ToString(CultureInfo.InvariantCulture);
                    break;
            }
            chart += "\n";
        }
        return chart;
    }
    
    /// <summary>
    /// 将字符串转为NoteInfo列表
    /// </summary>
    /// <param name="chart">谱面string</param>
    /// <returns></returns>
    /// <exception cref="ChartException">谱面读取异常</exception>
    public static List<NoteData> ToNoteInfoList(string chart)
    {
        try
        {
            return UnsafeToNoteInfoList(chart);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    //Unsafe
    private static List<NoteData> UnsafeToNoteInfoList(string chart)
    {
        List<NoteData> notesData = new List<NoteData>();
        
        //将行切割
        string[] lines = chart.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 0; i < lines.Length; i++)
        {
            //创建结构体
            NoteData noteData = new NoteData();
            
            //切割时间点信息
            string[] firstSplit = lines[i].Split(':');
            
            //尝试录入时间点信息
            if (int.TryParse(firstSplit[0], out noteData.TargetTime))
            {
    
            }
            else
            {
                throw new ChartException($"第{i+1}行：判定时间点读取异常！");
            }
            
            //尝试录入Note信息
            if (firstSplit.Length < 2)
            {
                throw new ChartException($"第{i+1}行：不存在Note！");
            }
            string[] thirdSplit = firstSplit[1].Split(',');
            

            //尝试录入轨道信息
            if (int.TryParse(thirdSplit[0], out noteData.LaneIndex))
            {
                        
            }
            else
            {
                throw new ChartException($"第{i+1}行：Note轨道读取异常！");
            }

            //检测轨道信息
            if (noteData.LaneIndex < -3 || noteData.LaneIndex > 3)
            {
                throw new ChartException($"第{i+1}行：Note轨道超出限制！");
            }


            //尝试录入类别信息
            if (thirdSplit.Length < 2)
            {
                throw new ChartException($"第{i+1}行：Note缺少类别信息！");
            }
            switch (thirdSplit[1])
            {
                case "t":
                    noteData.Type = NoteType.Dot;
                    break;
                case "h":
                {
                    noteData.Type = NoteType.Dash;
                
                    //尝试录入hold时间
                    if (thirdSplit.Length < 3)
                    {
                        throw new ChartException($"第{i+1}行：Note（hold）缺少Hold信息！");
                    }
                
                    if (int.TryParse(thirdSplit[2], out noteData.SustainDuration))
                    {
    
                    }
                    else
                    {
                        throw new ChartException($"第{i+1}行：HoldingTime读取异常！");
                    }

                    break;
                }
                case "d":
                {
                    noteData.Type = NoteType.Mute;
                
                    //尝试录入dash时间
                    if (thirdSplit.Length < 3)
                    {
                        throw new ChartException($"第{i+1}行：Note（dash）缺少Hold信息！");
                    }
                
                    if (int.TryParse(thirdSplit[2], out noteData.SustainDuration))
                    {
    
                    }
                    else
                    {
                        throw new ChartException($"第{i+1}行：HoldingTime读取异常！");
                    }

                    break;
                }
                default:
                    throw new ChartException($"第{i+1}行：Note类型不存在！");
            }
            //添加Note信息到表
            notesData.Add(noteData);
        }
        return  notesData;
    }
}
}

