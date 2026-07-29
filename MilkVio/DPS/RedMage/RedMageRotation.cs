using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.DPS.RedMage;
using MilkVio.DPS.RedMage.Action;
using MilkVio.DPS.RedMage.Action.Gcd;
using MilkVio.DPS.RedMage.Action.OffGcd;
using PromeRotation.Timeline;
using PromeRotation.Windows;
using MilkVio.Common;

namespace MilkVio.DPS.RedMage;

[RotationMetadata((uint)Job.RDM, "测试赤魔", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class RedMageRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new RedMageRotationEventHandler();
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
        {RDMData.RDMQt.启用起手 ,true},
        {RDMData.RDMQt.不打120 ,false},
        {RDMData.RDMQt.不打魔连击 ,false},
        
        {RDMData.RDMQt.AOE ,false},
        {RDMData.RDMQt.短兵相接 ,true},
        {RDMData.RDMQt.倾泻资源 ,false},
        
        {RDMData.RDMQt.促进 ,true},
        {RDMData.RDMQt.即刻咏唱 ,true},
        {RDMData.RDMQt.飞刺反击 ,true},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        
    };
    
    public RedMageRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 鼓励OffGcd());
        _offGcdResolvers.Add(new 魔元化OffGcd());
        _offGcdResolvers.Add(new 飞刺OffGcd());
        _offGcdResolvers.Add(new 六分反击OffGcd());
        _offGcdResolvers.Add(new 荆棘环绕OffGcd());
        _offGcdResolvers.Add(new 光芒四射OffGcd());
        _offGcdResolvers.Add(new 促进OffGcd());
        _offGcdResolvers.Add(new 即刻咏唱OffGcd());
        _offGcdResolvers.Add(new 交剑OffGcd());
        _offGcdResolvers.Add(new 短兵相接OffGcd());
        _offGcdResolvers.Add(new 醒梦OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 决断Gcd());
        _gcdResolvers.Add(new 焦热Gcd());
        _gcdResolvers.Add(new 赤核爆神圣Gcd());
        _gcdResolvers.Add(new 魔交击斩Gcd());
        _gcdResolvers.Add(new 魔连攻Gcd());
        _gcdResolvers.Add(new 魔回刺Gcd());
        _gcdResolvers.Add(new 促进赤雷_单体Gcd());
        _gcdResolvers.Add(new 促进赤风_单体Gcd());
        _gcdResolvers.Add(new 显贵冲击Gcd());
        _gcdResolvers.Add(new 赤火炎Gcd());
        _gcdResolvers.Add(new 赤飞石Gcd());
        _gcdResolvers.Add(new 震荡Gcd());
        _gcdResolvers.Add(new 赤雷_单体Gcd());
        _gcdResolvers.Add(new 赤风_单体Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        /*
         * GCD：
         * 决断
         * 焦热
         * 赤核爆/神圣
         * 魔划圆斩_AOE
         * 魔三连
         * 显贵冲击
         * 长读条雷风_AOE
         * 冲击_AOE
         * 赤火炎
         * 赤飞石
         * 激荡/震荡
         * 长读条雷风
         * 即刻/促进XX（可选）
         *
         * OGCD：
         * 鼓励
         * 魔元化
         * 飞刺
         * 六分反击
         * 促进xx
         * 即刻xx
         * 短兵相接
         * 交剑
         * 醒梦
         */
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(RDMData.RDMQt.启用起手) || Core.Me == null)
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
    }
    
    private void DrawDev()
    {
        ImGui.Text(RedMageHelper.GetNextManaActionPhase(false).ToString());
        ImGui.Text("促进：");
        ImGui.Text(RedMageHelper.GetPromoteStack().ToString());
        ImGui.Text("IsInManaActionCombo：");
        ImGui.Text(RedMageHelper.IsInManaActionCombo(Core.Me).ToString());
    }
    
}
