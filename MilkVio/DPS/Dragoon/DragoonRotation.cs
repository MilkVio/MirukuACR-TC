using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Helpers;
using MilkVio.DPS.Dragoon;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.DPS.Dragoon.Action.Gcd;
using MilkVio.DPS.Dragoon.Action.OffGcd;
using MilkVio.DPS.Dragoon.DRGData;
using MilkVio.DPS.Dragoon.Opener;
using MilkVio.DPS.UniversalData;
using PromeRotation.Timeline;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Updaters;
using PromeRotation.Windows;
using MilkVio.Common;

namespace MilkVio.DPS.Dragoon;

[RotationMetadata((uint)Job.DRG, "龙骑士苍天版", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class DragoonRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new DragoonRotationEventHandler();
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
        {DRGQt.启用起手, true},    //done
        {DRGQt.不打60, false},   //done
        {DRGQt.不打120, false},  //done
        {DRGQt.强制对齐爆发, false}, //done
        {DRGQt.双目标樱花, false},
        {DRGQt.坠星冲, true},//done
        {DRGQt.龙炎冲, true},//done
        {DRGQt.高跳, true},// done
        {DRGQt.天龙点睛, true},   // 增加最终爆发 todo 
        {DRGQt.贯穿尖, true},    // done
        {DRGQt.只打樱花连, false}, // done
        {DRGQt.只打直刺连, false}, // done
        {DRGQt.AOE, false},
        {DRGQt.最终爆发, false}, // todo
        {DRGQt.TP身位, false},
        {DRGQt.真北, true},
        {DRGQt.突进无位移, false},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        { "龙骑士70-80通用起手", typeof(DRG_7080_U) },
        { "龙骑士90通用起手", typeof(DRG_90_U) },
        { "龙骑士100通用起手", typeof(DRG_100_U) },
    };
    
    public DragoonRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 猛枪OffGcd());
        _offGcdResolvers.Add(new 战斗连祷OffGcd());
        _offGcdResolvers.Add(new 武神枪OffGcd());
        _offGcdResolvers.Add(new 天龙点睛OffGcd());
        _offGcdResolvers.Add(new 高跳OffGcd());
        _offGcdResolvers.Add(new 龙剑OffGcd());
        _offGcdResolvers.Add(new 龙炎冲OffGcd());
        _offGcdResolvers.Add(new 死者之岸OffGcd());
        _offGcdResolvers.Add(new 幻象冲OffGcd());
        _offGcdResolvers.Add(new 坠星冲OffGcd());
        _offGcdResolvers.Add(new 渡星冲OffGcd());
        _offGcdResolvers.Add(new 龙炎升OffGcd());
        _offGcdResolvers.Add(new 真北OffGcd());
        
        // 爆发状态下的GCD
        // 这个负责做决策打什么连击
        _gcdResolvers.Add(new AOEGcd());
        _gcdResolvers.Add(new 连击2Gcd());
        // 这个负责处理连击
        _gcdResolvers.Add(new 直刺连Gcd());
        _gcdResolvers.Add(new 贯穿尖Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "DRG Hotkeys");
        hotkeyPanel.AddHotkey("牵制", new PAction(MeleeUniversalSkill.牵制, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("真北", new PAction(MeleeUniversalSkill.真北, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("龙翼滑翔", new PAction(DRGSkill.龙翼滑翔, ActionType.OffGcd, ActionTargetType.Self));
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
        if (!PromeSettings.Instance.GetQt(DRGQt.启用起手) || Core.Me == null)
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
        if(ImGui.Button("关爆发"))
        {
            PromeSettings.Instance.SetQt(DRGQt.不打60, true);
            PromeSettings.Instance.SetQt(DRGQt.不打120, true);
        }
    }
    
    private void DrawDev()
    {
        ImGui.Text($"龙剑：{DRGSkill.龙剑.GetActionCharges()} {DRGSkill.龙剑.GetActionCooldown()}");
        ImGui.Text($"龙剑：{DRGSkill.龙剑.GetActionRecastTime()}");
        ImGui.Text($"龙剑：{ActionHelper.GetActionRecastTimeElapsed(DRGSkill.龙剑)}");
        ImGui.Text($"EyeCount：{JobGaugeHelper.DRG.EyeCount}");
        ImGui.Text($"龙眼（天龙点睛）：{JobGaugeHelper.DRG.FirstmindsFocusCount}");
        ImGui.Text($"是否在红龙血：{JobGaugeHelper.DRG.IsLOTDActive}");
        ImGui.Text($"红龙血剩余时间：{JobGaugeHelper.DRG.LOTDTimer}");
    }
    
}
