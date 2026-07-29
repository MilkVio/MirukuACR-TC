using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using MilkVio.DPS.Summoner.Action.Gcd;
using MilkVio.DPS.Summoner.Action.OffGcd;
using MilkVio.DPS.Summoner.Opener;
using MilkVio.DPS.Summoner.SMNData;
using PromeRotation.Timeline;
using MilkVio.Common;

namespace MilkVio.DPS.Summoner;

[RotationMetadata((uint)Job.SMN, "测试召唤", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class SummonerRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new SummonerRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => _eventHandler;
    
    // 管理该职业所有的决策解析器
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly OpenerSelector _openerSelector = new();
    
    // 实现对外暴露的静态属性
    // Qt列表
    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        {SMNQt.不打120, false},
        {SMNQt.龙神召唤, true},
        {SMNQt.不打三神, false},
        
        {SMNQt.AOE, false},
        {SMNQt.倾泻资源, false},
        {SMNQt.快打三神, false},
        
        {SMNQt.优先土神, false},
        {SMNQt.优先风神, false},
        {SMNQt.优先火神, false},
        
        {SMNQt.即刻火神, false},
        {SMNQt.即刻风圈, false},
        {SMNQt.深红旋风, true},
        
        {SMNQt.火神优先读条, false},
        {SMNQt.豆子, true},
        {SMNQt.突进无位移, false},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"召唤绝亚DollSkip起手", typeof(SMN_80_TEADS)},
        {"召唤绝伊甸起手", typeof(SMN_100_FRU)}
    };
    
    public SummonerRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 灼热之光OffGcd());
        _offGcdResolvers.Add(new 死星核爆_防溢出OffGcd());
        _offGcdResolvers.Add(new 龙神迸发_防溢出OffGcd());
        _offGcdResolvers.Add(new 山崩OffGcd());
        _offGcdResolvers.Add(new 即刻火神OffGcd());
        _offGcdResolvers.Add(new 即刻风圈OffGcd());
        _offGcdResolvers.Add(new 能量吸收OffGcd());
        _offGcdResolvers.Add(new 坏死爆发OffGcd());
        _offGcdResolvers.Add(new 灼热之闪OffGcd());
        _offGcdResolvers.Add(new 死星核爆OffGcd());
        _offGcdResolvers.Add(new 龙神迸发OffGcd());
        _offGcdResolvers.Add(new 醒梦OffGcd());
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 龙神召唤Gcd());
        _gcdResolvers.Add(new 星极脉冲Gcd());
        _gcdResolvers.Add(new 螺旋气流Gcd());
        _gcdResolvers.Add(new 深红旋风Gcd());
        _gcdResolvers.Add(new 深红强袭Gcd());
        _gcdResolvers.Add(new 三神召唤Gcd());
        _gcdResolvers.Add(new 毁绝Gcd());
        _gcdResolvers.Add(new 宝石耀Gcd());
        _gcdResolvers.Add(new 毁荡Gcd());
        _gcdResolvers.Add(new 宝石兽召唤Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        /*
         * GCD：
         * AOE龙喷
         * 龙喷
         * AOE宝石辉
         * 宝石耀
         * 三神召唤
         * 毁绝
         * 三重灾祸
         * 毁荡
         *
         * OGCD：
         * 灼热之光
         * 星极超流（龙神土神）
         * 豆子
         * 抽豆子
         * 龙神迸发
         * 灼热之闪
         */
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (Core.Me == null)
            return null;

        return _openerSelector.Resolve(Openers);
    }
    
    public PAction? NextAlways()
    {
        foreach (var resolver in _alwaysResolvers)
        {
            if (resolver.Check().Success)
                return resolver.GetAction();
        }
        return null;
    }

    public PAction? NextGcd()
    {
        // 遍历所有GCD解析器
        foreach (var resolver in _gcdResolvers)
        {
            if (resolver.Check().Success)
            {
                // 找到第一个满足条件的，返回它的决策结果
                return resolver.GetAction();
            }
        }
        // 如果所有求解器都不满足条件，返回null
        return null;
    }
    
    public PAction? NextOffGcd()
    {
        // 遍历所有oGCD解析器 同上
        foreach (var resolver in _offGcdResolvers)
        {
            if (resolver.Check().Success)
            {
                return resolver.GetAction();
            }
        }
        return null;
    }
    
    public void UpdateDebugStatus()
    {
        // 清空上一帧的旧数据
        RotationManager.AlwaysSolverStatus.Clear();
        RotationManager.GcdSolverStatus.Clear();
        RotationManager.OffGcdSolverStatus.Clear();

        foreach (var resolver in _alwaysResolvers)
        {
            var result = resolver.Check();

            RotationManager.AlwaysSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }
        
        // GCD状态列表
        foreach (var resolver in _gcdResolvers)
        {
            var result = resolver.Check();
            
            RotationManager.GcdSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }
        
        // OGCD状态列表
        foreach (var resolver in _offGcdResolvers)
        {
            var result = resolver.Check();
            
            RotationManager.OffGcdSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }
    }

    public void DrawQTs()
    {
        
    }

    public void DrawSettings()
    {
        if (ImGui.BeginTabBar("Settings"))
        {
            if (ImGui.BeginTabItem("设置"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("开发用"))
            {
                DrawDev();
                ImGui.EndTabItem();
            }
        }
    }

    private void DrawGeneral()
    {
        _openerSelector.DrawCombo("起手选择", Openers);
        ImGui.Text(SMNSkill.毁荡.GetAdjustedActionId().ToString());
    }
    
    private void DrawDev()
    {
        ImGui.Text($"HasPet：{JobGaugeHelper.SMN.HasPet}");
        ImGui.Text($"AetherflowStacks：{JobGaugeHelper.SMN.AetherflowStacks}");
        ImGui.Text($"SummonTimerRemaining：{JobGaugeHelper.SMN.SummonTimerRemaining}");
        ImGui.Text($"AttunementTimerRemaining：{JobGaugeHelper.SMN.AttunementTimerRemaining}");
        ImGui.Text($"AttunementCount：{JobGaugeHelper.SMN.AttunementCount}");
        ImGui.Text($"IsBahamutReady：{JobGaugeHelper.SMN.IsBahamutReady}");
        ImGui.Text($"IsPhoenixReady：{JobGaugeHelper.SMN.IsPhoenixReady}");
        ImGui.Text($"IsTitanReady：{JobGaugeHelper.SMN.IsTitanReady}");
        ImGui.Text($"IsGarudaReady：{JobGaugeHelper.SMN.IsGarudaReady}");
        ImGui.Text($"IsIfritReady：{JobGaugeHelper.SMN.IsIfritReady}");
        ImGui.Text($"IsTitanAttuned：{JobGaugeHelper.SMN.IsTitanAttuned}");
        ImGui.Text($"IsGarudaAttuned：{JobGaugeHelper.SMN.IsGarudaAttuned}");
        ImGui.Text($"IsIfritAttuned：{JobGaugeHelper.SMN.IsIfritAttuned}");
    }
    
}
