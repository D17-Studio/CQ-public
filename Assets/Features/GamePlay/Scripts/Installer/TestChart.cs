using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class TestChart
{
    public static string Get()
    {
        //在这里填写测试谱面
        string chartText = "";

        for (int i = 4; i < 32; i++)
        {
            chartText += $"Dash - Time:{(60000 * i / 170) + 100} ; Lane:-3 ; Duration:{60000/170/2} ; Appear:-3000 ; Speed:Default()\n";
        }
        
        for (int i = 4; i < 32; i++)
        {
            chartText += $"Dot - Time:{(60000 * i / 170) + 100} ; Lane:-2 ; Duration:0 ; Appear:-3000 ; Speed:Multiplier({1+(i-4)/10f})\n";
        }
        
        for (int i = 4; i < 32; i++)
        {
            chartText += $"Mute - Time:{(60000 * i / 170) + 100} ; Lane:-1 ; Duration:{60000/170/4} ; Appear:-3000 ; Speed:Fixed(2000)\n";
        }
        
        for (int i = -3; i <= 3; i++)
        {
            chartText += $"LaneMotion - Time:-1 ; Lane:{i} ; Duration:0 ; Anchor:(0,0) ; Position:[Absolute,({i*100},-300),Linear]  ; Opacity:[Affect,(Head,Line,Key,Note),1,Linear]\n";
        }
        
        for (int i = -3; i <= 3; i++)
        {
            chartText += $"LaneMotion - Time:{(60000 * 8 / 170) + 100} ; Lane:{i} ; Duration:500 ; Anchor:(0,0) ; Position:[Absolute,({i*150},-300),InOutBack]\n";
        }
        
        for (int i = -3; i <= 3; i++)
        {
            chartText += $"LaneMotion - Time:{(60000 * 12 / 170) + 100 + 50*i} ; Lane:{i} ; Duration:500 ; Anchor:(0,0) ; Position:[Absolute,({i*200},-400),OutCubic]\n";
        }
        for (int i = -3; i <= 3; i++)
        {
            chartText += $"LaneMotion - Time:{(60000 * 16 / 170) + 100 + 50*i} ; Lane:{i} ; Duration:500 ; Anchor:(0,0) ; Position:[Absolute,({i*200},-400),OutCubic] ; Rotation:[Absolute,{i*5},OutCubic]\n";
        }
        
        // for (int i = -3; i <= 0; i++)
        // {
        //     chartText += $"LaneMotion - Time:{(60000 * 20 / 170) + 100 + 50*i} ; Lane:{i} ; Duration:300 ; Anchor:(0,500) ; Position:[Relative,(0,0),OutCubic] ; Rotation:[Relative,360,OutBack]\n";
        // }

        
        for (int i = -3; i <= 3; i++)
        {
            chartText += $"LaneMotion - Time:{(60000 * 20 / 170) + 100 } ; Lane:{i} ; Duration:300 ; Anchor:(0,900) ; Position:[Absolute,(0,400),OutCubic] ; Rotation:[Absolute,{i*15},OutCubic]\n";
        }
        
        for (int i = 1; i < 10; i++)
        {
            chartText += $"Dash - Time:{(60000 * (i*4) / 170 ) + 100} ; Lane:2 ; Duration:{60000/170*3} ; Appear:-3000 ; Speed:Default()\n";
            chartText += $" - Tune - Time:{(int)(60000 * (i * 4 + 0.5) / 170) + 100} ; Direction:Left ; Motion:Relative(1,(0,50)) ; Duration:{60000/2/170}\n";
            chartText += $" - Tune - Time:{(int)(60000 * (i * 4 + 1) / 170) + 100} ; Direction:Left ; Motion:Relative(3,(0,100)) ; Duration:{60000/2/170}\n";
            chartText += $" - Tune - Time:{(int)(60000 * (i * 4 + 1.5) / 170) + 100} ; Direction:Left ; Motion:Relative(2,(0,150)) ; Duration:{60000/2/170/5}\n";
        }
        
        return chartText;
    }
}
