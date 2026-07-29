using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;
using PromeRotation.Data;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.DPS.Dancer.DNCData;
using MilkVio.DPS.Dancer.Action.Gcd;
using MilkVio.DPS.Dancer.Action.OffGcd;
using MilkVio.DPS.UniversalData;
using PromeRotation.Timeline;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Updaters;
using PromeRotation.Windows;
using MilkVio.Common;

namespace MilkVio.DPS.Dancer;

[RotationMetadata((uint)Job.DNC, "舞者勾吸版", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class DancerRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new DancerRotationRotationEventHandler();
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
        {DNCQt.启用起手, true},
        {DNCQt.不打扇子, false},
        {DNCQt.倾泻资源, false}, 
        {DNCQt.先打大舞, false},
        {DNCQt.不打120, false},
        {DNCQt.不打百花, false},
        {DNCQt.强制大舞, false},
        {DNCQt.强制小舞, false},
        {DNCQt.强制小舞和打出, false},
        {DNCQt.启用多目标, false},
        {DNCQt.不打小舞, false},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"通用起手", typeof(DNCopener)}
    };
    
    public DancerRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        // _offGcdResolvers.Add(new SimpleOffGcd());
        _offGcdResolvers.Add(new 探戈OffGcd());
        _offGcdResolvers.Add(new 百花OffGcd());
        _offGcdResolvers.Add(new 扇舞急和扇舞终OffGcd());
        _offGcdResolvers.Add(new 扇舞序破OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 小舞结束Gcd());
        _gcdResolvers.Add(new 大舞结束Gcd());
        _gcdResolvers.Add(new 小舞跳舞Gcd());
        _gcdResolvers.Add(new 大舞跳舞Gcd());
        _gcdResolvers.Add(new 大舞释放Gcd());
        _gcdResolvers.Add(new 小舞和结束动作释放Gcd());
        _gcdResolvers.Add(new 团辅剑舞溢出Gcd());
        _gcdResolvers.Add(new 拂晓舞Gcd());
        _gcdResolvers.Add(new 流星舞Gcd());
        _gcdResolvers.Add(new 提拉那Gcd());
        _gcdResolvers.Add(new 剑舞Gcd());
        _gcdResolvers.Add(new 基础AOEGcd());
        _gcdResolvers.Add(new 基础Gcd());
        
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "DNC Hotkeys");
        hotkeyPanel.AddHotkey("防守之桑巴", new PAction(DNCSkill.桑巴, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("前冲步", new PAction(16010, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("疾跑", new PAction(3, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("亲疏自行", new PAction(MeleeUniversalSkill.亲疏自行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("内丹", new PAction(MeleeUniversalSkill.内丹, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey(
            "同步镜头",
            new ToggleLogic(
                () => CameraSyncManager.CurrentMode == SyncMode.Camera,
                CameraSyncManager.ToggleCameraSync),
            iconActionId: 11404);
        hotkeyPanel.AddHotkey(
            "校准正方向",
            new ToggleLogic(
                () => CameraSyncManager.CurrentMode == SyncMode.Align,
                CameraSyncManager.ToggleAlignSync),
            customIconPath: "Resources/Align4.png");
        hotkeyPanel.AddHotkey(
            "清扫队列",
            new ExecuteLogic(() =>
            {
                ActionQueueManager.ClearAllQueues();
                ActionUpdater.Reset();
                Svc.Chat.PrintError("[PromeRotation] 清扫队列");
            }),
            customIconPath: "Resources/Clear.png");
        HotkeyManager.Instance.AddHotkeyPanel(hotkeyPanel);
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(DNCQt.启用起手) || Core.Me == null)
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
        var steps = JobGaugeHelper.DNC.Steps;
        ImGui.Text($"幻扇数：{JobGaugeHelper.DNC.Feathers}");
        ImGui.Text($"伶俐值：{JobGaugeHelper.DNC.Esprit}");
        ImGui.Text($"当前是否在跳舞：{JobGaugeHelper.DNC.IsDancing}");
        ImGui.Text($"完成该舞蹈需要的步骤数：{JobGaugeHelper.DNC.CompletedSteps}");
        ImGui.Text($"该舞蹈所需的所有技能Id：");
        foreach (var s in steps)
        {
            ImGui.SameLine();
            ImGui.Text(s.ToString());
        }
        ImGui.Text($"下一个舞蹈的技能Id：{JobGaugeHelper.DNC.NextStep}");
    }
    
}
