using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.GNB;
using MilkVio.GNB.Opener;
using MilkVio.Tank.GNB.Action.Gcd;
using MilkVio.Tank.GNB.Action.OffGcd;
using PromeRotation.Timeline;
using PromeRotation.Windows;

namespace MilkVio.Tank.GNB;

[RotationMetadata((uint)Job.GNB, "绝枪战士测试版", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class GunbreakerRotation// : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new GunbreakerRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => _eventHandler;
    
    // 管理该职业所有的决策解析器
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    
    // 实现对外暴露的静态属性
    // Qt列表
    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        
    };
    
    public GunbreakerRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的求解器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 烈牙OffGcd());
        _offGcdResolvers.Add(new OffGcdTest());
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 烈牙Gcd());
        _gcdResolvers.Add(new 狮心连Gcd());
        _gcdResolvers.Add(new BaseGcd());
        
        PromeSettings.Instance.AddQt("不打烈牙", false);
        PromeSettings.Instance.AddQt("启用起手", true);
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt("启用起手") && Core.Me == null)
            return null;

        // PTL 起手优先：PTL 未提供 Opener 时才回退旧 Timeline Meta。
        var openerName = PromeRotation.PureTimeline.PtlManager.CurrentOpener;
        var openerSource = "PureTimeline";

        if (string.IsNullOrWhiteSpace(openerName))
        {
            var meta = TimelineManager.CurrentMeta;
            openerName = meta?.Opener;
            openerSource = "Timeline";
        }

        if (string.IsNullOrWhiteSpace(openerName))
            return null;
        
        // 查找对应起手类型（来自当前职业的 Meta.Openers）
        var openers = RotationManager.GetOpenersByJob((int)Core.Me.ClassJob.RowId);
        if (openers == null || !openers.TryGetValue(openerName, out var openerType))
        {
            PluginLog.Warning($"[ACR] {openerSource} 指定起手不存在：{openerName}");
            return null;
        }

        try
        {
            // 动态创建对应起手类实例
            if (Activator.CreateInstance(openerType) is IOpener opener)
            {
                PluginLog.Information($"[ACR] 从{openerSource} Meta 加载起手：{openerName}");
                return opener;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[ACR] 创建起手实例失败: {ex.Message}");
        }

        return null;
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
        
    }
    
    private void DrawDev()
    {
        ImGui.Text(GunbreakerHelper.GetLieFangStack().ToString());
    }
    
}
