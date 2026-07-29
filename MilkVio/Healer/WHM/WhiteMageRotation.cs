using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.Healer.WHM.Action.GCD;
using MilkVio.Healer.WHM.Action.OGCD;
using PromeRotation.Timeline;
using PromeRotation.Windows;
using MilkVio.Common;

namespace MilkVio.Healer.WHM;

[RotationMetadata((uint)Job.WHM, "逆天炒股白魔", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public sealed class WhiteMageRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new WhiteMageRotationEventHandler();
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
        {WHMData.WHMQt.启用起手,  true},
        {WHMData.WHMQt.不打120,  false},
        {WHMData.WHMQt.AOE,  true},
        {WHMData.WHMQt.续毒移动,  true},
        {WHMData.WHMQt.只打闪灼,  false},
        {WHMData.WHMQt.只打续毒,  false},
        {WHMData.WHMQt.法令,  true},
        {WHMData.WHMQt.蓝花,  true},
        {WHMData.WHMQt.红花,  true},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"白魔90级绝欧起手", typeof(WHM_90_TOP)},
    };
    
    public WhiteMageRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 神速咏唱OffGcd());
        _offGcdResolvers.Add(new 法令OffGcd());
        _offGcdResolvers.Add(new 醒梦OffGcd());
        
        // 爆发状态下的GCD
        // _gcdResolvers.Add(new SimpleGcd());
        _gcdResolvers.Add(new 闪飒Gcd());
        _gcdResolvers.Add(new 苦难之心Gcd());
        _gcdResolvers.Add(new 毒Gcd());
        _gcdResolvers.Add(new 闪灼Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(WHMData.WHMQt.启用起手) || Core.Me == null)
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
        ImGui.Text($"Lily:{JobGaugeHelper.WHM.Lily}");
        ImGui.Text($"BloodLily:{JobGaugeHelper.WHM.BloodLily}");
        ImGui.Text($"LilyTimer:{JobGaugeHelper.WHM.LilyTimer}");
    }
    
}
