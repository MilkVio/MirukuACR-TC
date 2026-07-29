using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.Tank.PLD.Action.Gcd;
using MilkVio.Tank.PLD.Action.OffGcd;
using MilkVio.Tank.PLD.Opener;
using MilkVio.Tank.PLD.PLDData;
using PromeRotation.Helpers;
using PromeRotation.Timeline;
using PromeRotation.Timeline.Core;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Windows;
using MilkVio.Common;
using AOEGcd = MilkVio.DPS.Dragoon.Action.Gcd.AOEGcd;

namespace MilkVio.Tank.PLD;

[RotationMetadata((uint)Job.PLD, "玉玉骑士", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class PaladinRotation : IRotation
{
    public static IJobNodeProvider? NodeProvider { get; } = new PaladinJobNodeProvider();
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new PaladinRotationEventHandler();
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
        {PLDQt.启用起手, true},
        {PLDQt.远离圣灵, true},
        {PLDQt.倾泻资源, false},
        {PLDQt.延后大宝剑, false},
        {PLDQt.硬读圣灵, false},
        {PLDQt.AOE, true},
        {PLDQt.不打60, false},
        {PLDQt.调停, true},
        {PLDQt.最优战逃, true},
        {PLDQt.不打双能力技, false},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"骑士通用起手", typeof(Opener_PLD_General)},
        {"骑士绝亚DollSkip起手", typeof(PLD_80_TEADS_ST)},
        {"骑士绝欧ST起手", typeof(PLD_90_TOP_ST)},
        {"骑士绝伊甸ST起手2", typeof(PLD_100_FRU_ST_FA)},
    };
    
    public PaladinRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 战逃反应OffGcd());
        _offGcdResolvers.Add(new 安魂祈祷OffGcd());
        _offGcdResolvers.Add(new 荣耀之剑OffGcd());
        _offGcdResolvers.Add(new 偿赎剑OffGcd());
        _offGcdResolvers.Add(new 厄运流转OffGcd());
        _offGcdResolvers.Add(new 调停OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 安魂Gcd());
        _gcdResolvers.Add(new 沥血剑Gcd());
        _gcdResolvers.Add(new 圣环Gcd());
        _gcdResolvers.Add(new Action.Gcd.AOEGcd());
        _gcdResolvers.Add(new 圣灵Gcd());
        _gcdResolvers.Add(new 强化三连Gcd());
        _gcdResolvers.Add(new 基础Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "PLD Hotkeys");
        hotkeyPanel.AddHotkey("调停", new PAction(PLDSkill.调停, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("挑衅", new PAction(DRKSkill.挑衅, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey(
            "退避ST",
            new ExecuteLogic(() =>
                ActionQueueManager.Enqueue(
                    new PAction(DRKSkill.退避, ActionType.OffGcd, ActionTargetType.PartyMember2), true)),
            iconActionId: DRKSkill.退避,
            customIconPath: "Resources/DRK/TBST.png");
        hotkeyPanel.AddHotkey("圣盾阵", new PAction(PLDSkill.圣盾阵, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("壁垒", new PAction(PLDSkill.壁垒, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("铁壁", new PAction(DRKSkill.铁壁, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("极致防御", new PAction(PLDSkill.极致防御, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("亲疏自行", new PAction(DRKSkill.亲疏自行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("血仇", new PAction(DRKSkill.血仇, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("圣光幕帘", new PAction(PLDSkill.圣光幕帘, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey(
            "干预ST",
            new ExecuteLogic(() =>
                ActionQueueManager.Enqueue(
                    new PAction(PLDSkill.干预, ActionType.OffGcd, ActionTargetType.PartyMember2), true)),
            iconActionId: PLDSkill.干预,
            customIconPath: "Resources/DRK/HDST.png");
        hotkeyPanel.AddHotkey("钢铁信念", new PAction(PLDSkill.钢铁信念, ActionType.OffGcd, ActionTargetType.Self));
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
                Svc.Chat.PrintError("[PromeRotation] 清扫队列");
            }),
            customIconPath: "Resources/Clear.png");
        HotkeyManager.Instance.AddHotkeyPanel(hotkeyPanel);
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(PLDQt.启用起手) || Core.Me == null)
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
        ImGui.Text(Core.Me.HasStatus(PLDBuff.战逃反应buff).ToString());
        ImGui.Text(PLDSkill.调停.GetActionCharges().ToString());
        ImGui.Text(ActionHelper.ResolveActionType(PLDSkill.先锋剑).ToString());
        ImGui.Text(ActionHelper.ResolveActionType(PLDSkill.调停).ToString());
    }
    
    private void DrawDev()
    {
        
    }
    
}
