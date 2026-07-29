using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using MilkVio.Healer.Common;
using MilkVio.Healer.SCH;
using MilkVio.Healer.SCH.Openers;
using MilkVio.Healer.SCH.Resolvers.Gcd;
using MilkVio.Healer.SCH.Resolvers.OffGcd;
using MilkVio.Healer.SCH.Timeline;
using PromeRotation.Data;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using PromeRotation.Timeline;
using PromeRotation.Timeline.Core;

namespace MilkVio.Healer.SCH;

[RotationMetadata((uint)Job.SCH, "Mio Scholar", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public sealed class ScholarRotation : IRotation
{
    private readonly IRotationEventHandler _eventHandler = new DefaultRotationEventHandler();
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly ScholarRotationContext _context = new();

    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        { HealerQt.启用起手, true },
        { HealerQt.停手, false },
        { HealerQt.爆发药, false },
        { HealerQt.AOE, true },
        { HealerQt.DOT, true },
        { HealerQt.毁2, true },
        { HealerQt.康复, true },
        { HealerQt.朝日召唤, true },
        { HealerQt.以太超流, true },
        { HealerQt.转化, true },
        { HealerQt.能量吸收, true },
        { HealerQt.醒梦, true },
        { HealerQt.连环计, true },
        { HealerQt.即刻极炎法, false },
    };

    private static readonly string[] OpenerNames =
    [
        学者Ultimate起手.OpenerDisplayName,
    ];

    private static readonly string[] ShieldOpenerNames =
    [
        "扩散盾预铺",
        "无预铺",
        "群盾预铺",
    ];

    private static readonly string[] ResourceOpenerNames =
    [
        "转化起手",
        "以太起手",
    ];

    private static readonly string[] SwiftOpenerNames =
    [
        "即刻起手",
        "无即刻起手",
    ];

    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        { OpenerNames[0], typeof(学者Ultimate起手) },
    };

    public static IJobNodeProvider? NodeProvider { get; } = new ScholarJobNodeProvider();

    public ScholarRotation()
    {
        // 在这里，按照优先级从高到低注册学者 oGCD 解析器。
        _offGcdResolvers.Add(new 即刻极炎法OffGcd());
        _offGcdResolvers.Add(new 连环计OffGcd());
        _offGcdResolvers.Add(new 毒炎冲击OffGcd());
        _offGcdResolvers.Add(new 能量吸收OffGcd());
        _offGcdResolvers.Add(new 转化OffGcd());
        _offGcdResolvers.Add(new 以太超流OffGcd());
        _offGcdResolvers.Add(new 醒梦OffGcd());

        // 在这里，按照优先级从高到低注册学者 GCD 解析器。
        _gcdResolvers.Add(new 康复Gcd(_context));
        _gcdResolvers.Add(new 召唤朝日Gcd());
        _gcdResolvers.Add(new 破阵法Gcd());
        _gcdResolvers.Add(new 蛊毒法Gcd());
        _gcdResolvers.Add(new 毁坏Gcd());
        _gcdResolvers.Add(new 极炎法Gcd());

        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
    }

    public IRotationEventHandler GetEventHandler() => _eventHandler;

    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(HealerQt.启用起手) || Core.Me == null)
            return null;

        var openerName = PromeRotation.PureTimeline.PtlManager.CurrentOpener;
        var openerSource = "PureTimeline";

        if (string.IsNullOrWhiteSpace(openerName))
        {
            var meta = TimelineManager.CurrentMeta;
            openerName = meta?.Opener;
            openerSource = "Timeline";
        }

        if (string.IsNullOrWhiteSpace(openerName))
        {
            openerName = SelectedOpenerName();
            openerSource = "设置页";
        }

        if (!Openers.TryGetValue(openerName, out var openerType))
        {
            PluginLog.Warning($"[ACR] {openerSource} 指定起手不存在：{openerName}");
            return null;
        }

        try
        {
            if (Activator.CreateInstance(openerType) is IOpener opener)
            {
                PluginLog.Information($"[ACR] 从{openerSource}加载起手：{openerName}");
                return opener;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[ACR] 创建起手实例失败: {ex.Message}");
        }

        return null;
    }

    public PAction? NextAlways() => Resolve(_alwaysResolvers);

    public PAction? NextGcd() => Resolve(_gcdResolvers);

    public PAction? NextOffGcd() => Resolve(_offGcdResolvers);

    public void UpdateDebugStatus()
    {
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

    public void DrawQTs() => DrawGeneral();

    public void DrawSettings()
    {
        if (!ImGui.BeginTabBar($"Settings##{nameof(ScholarRotation)}")) return;

        if (ImGui.BeginTabItem("设置"))
        {
            DrawSchSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("开发用"))
        {
            DrawDev();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static string SelectedOpenerName()
        => OpenerNames[0];

    private static void DrawSchSettings()
    {
        DrawComboSetting("预铺方式", ShieldOpenerNames, MioAcrSettings.Instance.SchShieldOpenerIndex, value =>
        {
            MioAcrSettings.Instance.SchShieldOpenerIndex = value;
            MioAcrSettings.Save();
        });

        DrawComboSetting("资源起手", ResourceOpenerNames, MioAcrSettings.Instance.SchResourceOpenerIndex, value =>
        {
            MioAcrSettings.Instance.SchResourceOpenerIndex = value;
            MioAcrSettings.Save();
        });

        DrawComboSetting("即刻起手", SwiftOpenerNames, MioAcrSettings.Instance.SchSwiftOpenerIndex, value =>
        {
            MioAcrSettings.Instance.SchSwiftOpenerIndex = value;
            MioAcrSettings.Save();
        });

        var reserve = Math.Clamp(MioAcrSettings.Instance.SchAetherflowReserve, 0, 3);
        if (ImGui.InputInt("能量吸收保留豆", ref reserve))
        {
            MioAcrSettings.Instance.SchAetherflowReserve = Math.Clamp(reserve, 0, 3);
            MioAcrSettings.Save();
        }

        var isH1 = MioAcrSettings.Instance.SchIsH1;
        if (ImGui.Checkbox("康复使用H1优先级", ref isH1))
        {
            MioAcrSettings.Instance.SchIsH1 = isH1;
            MioAcrSettings.Save();
        }
    }

    private static void DrawComboSetting(string label, IReadOnlyList<string> names, int currentIndex, Action<int> setValue)
    {
        var selectedIndex = Math.Clamp(currentIndex, 0, names.Count - 1);
        if (!ImGui.BeginCombo(label, names[selectedIndex])) return;

        for (var i = 0; i < names.Count; i++)
        {
            var isSelected = i == selectedIndex;
            if (ImGui.Selectable(names[i], isSelected))
                setValue(i);

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawGeneral()
    {
        foreach (var (name, _) in QtList)
        {
            var value = PromeSettings.Instance.GetQt(name);
            if (ImGui.Checkbox(name, ref value))
                PromeSettings.Instance.SetQt(name, value);
        }
    }

    private void DrawDev()
    {
        var me = HealerUtils.Me;
        ImGui.Text($"Job:{(uint)Job.SCH}");
        ImGui.Text($"Level:{me?.Level ?? 0}");
        ImGui.Text($"MP:{me?.CurrentMp ?? 0}");
        ImGui.Text($"PromeSCHGauge:{HealerUtils.HasSchPromeGauge()}");
        ImGui.Text($"Aetherflow:{HealerUtils.SchAetherflow()}");
        ImGui.Text($"FairyGauge:{HealerUtils.SchFairyGauge()}");
        ImGui.Text($"SeraphTimer:{HealerUtils.SchSeraphTimer()}");
        ImGui.Text($"DismissedFairy:{HealerUtils.SchDismissedFairyText()}");
        ImGui.Text($"HasPet:{HealerUtils.SchHasPet()?.ToString() ?? "Unknown"}");
        ImGui.Text($"Reserve:{Math.Clamp(MioAcrSettings.Instance.SchAetherflowReserve, 0, 3)}");
    }

    private static PAction? Resolve(IEnumerable<IDecisionResolver> resolvers)
    {
        foreach (var resolver in resolvers)
        {
            if (resolver.Check().Success)
                return resolver.GetAction();
        }

        return null;
    }
}
