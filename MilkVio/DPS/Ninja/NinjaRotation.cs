using System;
using System.Collections.Generic;
using ImGuiNET;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using MilkVio.Common;
using MilkVio.DPS.Ninja;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.DPS.Ninja.Action.Gcd;
using MilkVio.DPS.Ninja.Action.OffGcd;
using MilkVio.DPS.Ninja.NinjaData;
using MilkVio.DPS.Ninja.Opener;
using MilkVio.DPS.UniversalData;
using PromeRotation.Timeline;
using PromeRotation.Timeline.Core;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Updaters;
using PromeRotation.Windows;

namespace MilkVio.DPS.Ninja;

[RotationMetadata((uint)Job.NIN, "忍者测试版", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class NinjaRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new NinjaRotationEventHandler();
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
        {NinjaQt.启用起手, true},
        {NinjaQt.不打60, false},
        {NinjaQt.不打120, false},
        
        {NinjaQt.倾泻资源, false},
        {NinjaQt.水遁, true},
        {NinjaQt.忍术不溢出, false},
        
        {NinjaQt.AOE, false},
        {NinjaQt.分身之术, true},
        {NinjaQt.镰鼬对齐120, false},
        
        {NinjaQt.真北, true},
        {NinjaQt.TP身位, false},
        {NinjaQt.远离飞刀, false},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"忍者100通用2G爆发起手", typeof(NIN_100_G)},
        {"忍者90通用2G爆发起手", typeof(NIN_90_U)},
        {"忍者90欧米茄2G爆发起手", typeof(NIN_90_OMG)},
        {"忍者80通用2G爆发起手", typeof(NIN_80_U)},
    };
    
    public static IJobNodeProvider? NodeProvider { get; } = new NinjaJobNodeProvider();
    
    public NinjaRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 介毒OffGcd());
        _offGcdResolvers.Add(new 攻其百雷OffGcd());
        _offGcdResolvers.Add(new 天地人OffGcd());
        _offGcdResolvers.Add(new 生杀OffGcd());
        _offGcdResolvers.Add(new 分身之术OffGcd());
        _offGcdResolvers.Add(new 命水OffGcd());
        _offGcdResolvers.Add(new 秘技蛤蟆OffGcd());
        _offGcdResolvers.Add(new 是生灭法OffGcd());
        _offGcdResolvers.Add(new 梦幻三段OffGcd());
        _offGcdResolvers.Add(new 普通蛤蟆OffGcd());
        _offGcdResolvers.Add(new 六道轮回OffGcd());
        _offGcdResolvers.Add(new 天理人道OffGcd());
        _offGcdResolvers.Add(new 真北OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 天地人Gcd());
        _gcdResolvers.Add(new 释放忍术Gcd());
        _gcdResolvers.Add(new 残影镰鼬_防溢出Gcd());
        _gcdResolvers.Add(new 水遁Gcd());
        _gcdResolvers.Add(new 劫火遁Gcd());
        _gcdResolvers.Add(new 冰晶Gcd());
        _gcdResolvers.Add(new 火遁Gcd());
        _gcdResolvers.Add(new 雷遁Gcd());
        _gcdResolvers.Add(new 月影雷兽Gcd());
        _gcdResolvers.Add(new 残影镰鼬Gcd());
        _gcdResolvers.Add(new AOEGcd());
        _gcdResolvers.Add(new 强甲破点突Gcd());
        _gcdResolvers.Add(new 旋风刃Gcd());
        _gcdResolvers.Add(new 基础12Gcd());
        _gcdResolvers.Add(new 飞刀Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "NIN Hotkeys");
        hotkeyPanel.AddHotkey("牵制", new PAction(MeleeUniversalSkill.牵制, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("真北", new PAction(MeleeUniversalSkill.真北, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("残影", new PAction(NinjaSkill.残影, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("疾跑", new PAction(3, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("亲疏自行", new PAction(MeleeUniversalSkill.亲疏自行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("内丹", new PAction(MeleeUniversalSkill.内丹, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("浴血", new PAction(MeleeUniversalSkill.浴血, ActionType.OffGcd, ActionTargetType.Self));
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
        if (!PromeSettings.Instance.GetQt(NinjaQt.启用起手) || Core.Me == null)
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
        ImGui.Text($"{NinjaHelper.GetCurrentNinjaNinjyutsuCharge().ToString()}");
    }
    
}
