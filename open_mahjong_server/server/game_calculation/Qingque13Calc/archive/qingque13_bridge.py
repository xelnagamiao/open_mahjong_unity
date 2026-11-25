"""
Qingque13 C# 程序集桥接模块
处理 pythonnet 的导入、DLL 加载和类型绑定
"""
import os
import threading
from pathlib import Path
from typing import Optional

# pythonnet 相关导入和初始化
try:
    # 重要：在导入 clr 之前设置环境变量，强制使用 .NET Core 运行时
    # 这对于 .NET 6+ 编译的 DLL 是必需的
    if 'PYTHONNET_RUNTIME' not in os.environ:
        os.environ['PYTHONNET_RUNTIME'] = 'coreclr'
    
    import clr  # type: ignore
    
    from System.Reflection import BindingFlags  # type: ignore
    from System.Collections.Generic import List  # type: ignore
    from System import Int32, Boolean  # type: ignore
    
    _PYTHONNET_AVAILABLE = True
except Exception:
    clr = None  # type: ignore
    BindingFlags = None  # type: ignore
    List = None  # type: ignore
    Int32 = None  # type: ignore
    Boolean = None  # type: ignore
    _PYTHONNET_AVAILABLE = False

# C# 类型引用（将在运行时绑定）
Qingque13Hepai: Optional[type] = None
Qingque13Tingpai: Optional[type] = None
_qingque13_loaded = False
_load_lock = threading.Lock()  # 保护 DLL 加载过程的线程锁


def _ensure_qingque13_loaded() -> None:
    """
    使用 pythonnet 加载 Qingque13 程序集，并绑定 Qingque13Hepai / Qingque13Tingpai。
    优先使用环境变量 QINGQUE13_DLL_PATH，其次尝试相对路径和程序集名称。
    线程安全：使用锁确保多线程环境下只加载一次。
    """
    global _qingque13_loaded, Qingque13Hepai, Qingque13Tingpai
    
    # 快速路径：如果已加载，直接返回（避免每次调用都获取锁）
    if _qingque13_loaded:
        return
    
    # 使用锁保护加载过程，确保多线程环境下只加载一次
    with _load_lock:
        # 双重检查：获取锁后再次检查，避免多个线程同时通过第一次检查
        if _qingque13_loaded:
            return

    if not _PYTHONNET_AVAILABLE or clr is None:
        raise RuntimeError(
            "未安装 pythonnet（模块 'clr'），无法调用 Qingque13 C# 逻辑，请先执行 'pip install pythonnet'。"
        )
    
    # 诊断信息：检查 pythonnet 版本和运行时
    try:
        pythonnet_version = getattr(clr, '__version__', 'unknown')
        print(f"[DEBUG] pythonnet 版本: {pythonnet_version}")
        
        # 检查运行时类型
        try:
            from System import Runtime  # type: ignore
            print(f"[DEBUG] .NET 运行时信息: {Runtime.InteropServices.RuntimeInformation.FrameworkDescription}")
        except Exception:
            pass
    except Exception:
        pass

    # 1. 如果用户通过环境变量显式指定了 dll 路径，则优先使用
    dll_path = os.environ.get("QINGQUE13_DLL_PATH")
    asm = None

    if dll_path:
        dll_path = os.path.abspath(dll_path)
        if not os.path.exists(dll_path):
            raise RuntimeError(f"环境变量 QINGQUE13_DLL_PATH 指向的文件不存在: {dll_path}")
        print(f"[DEBUG] 从环境变量加载 DLL: {dll_path}")
        asm = clr.AddReference(dll_path)  # type: ignore
    else:
        # 2. 尝试在当前目录附近寻找 DLL
        base_dir = Path(__file__).resolve().parent
        workspace_root = base_dir.parents[3] if len(base_dir.parents) >= 4 else base_dir
        unity_asm = workspace_root / "open_mahjong_unity" / "open_mahjong_unity" / "Library" / "ScriptAssemblies" / "Assembly-CSharp.dll"

        # 注意：dll 文件名/程序集名可以叫 Qingque13Calc，但 **命名空间** 是 Qingque13
        candidates = [
            base_dir / "Qingque13Calc" / "Qingque13Calc.dll",  # 你现在放置的位置
            base_dir / "Qingque13Calc.dll",  # 也可能在同级目录
            base_dir / "Qingque13.dll",
            base_dir / "Qingque.dll",
            unity_asm,
        ]
        loaded_dll_path = None
        for cand in candidates:
            if cand.is_file():
                loaded_dll_path = str(cand)
                print(f"[DEBUG] 正在加载 DLL: {loaded_dll_path}")
                asm = clr.AddReference(loaded_dll_path)  # type: ignore
                break

        # 3. 如果没找到物理文件，尝试按程序集名加载（需用户在其他地方已注册搜索路径）
        if asm is None:
            try:
                asm = clr.AddReference("Qingque13Calc")  # type: ignore
            except Exception:
                try:
                    asm = clr.AddReference("Qingque13")  # type: ignore
                except Exception:
                    pass

        if asm is None:
            raise RuntimeError(
                "未能自动加载 Qingque13 程序集，请：\n"
                "1) 将 Qingque13 的 dll 路径写入环境变量 QINGQUE13_DLL_PATH，或\n"
                "2) 在程序启动阶段手动调用 clr.AddReference(...) 后再使用 QQ13_* 接口。"
            )

    # 4. 程序集加载成功后，通过反射获取类型（不依赖 Python 模块导入）
    try:
        from System import Type  # type: ignore

        # 调试：列出程序集中的所有类型
        try:
            if hasattr(asm, 'GetTypes'):
                all_types = [t.FullName for t in asm.GetTypes() if t.FullName is not None]
                print(f"[DEBUG] 程序集 '{asm.FullName}' 中包含的类型（前20个）:")
                for tname in sorted(all_types)[:20]:
                    print(f"  - {tname}")
                if len(all_types) > 20:
                    print(f"  ... 共 {len(all_types)} 个类型")
            else:
                print(f"[DEBUG] 程序集对象: {asm} (类型: {type(asm)})")
        except Exception as debug_exc:
            print(f"[DEBUG] 无法列出程序集类型: {debug_exc}")

        # 完整限定名必须与 C# 中的 namespace + class 名匹配
        hepai_type = asm.GetType("Qingque13.Qingque13Hepai")
        tingpai_type = asm.GetType("Qingque13.Qingque13Tingpai")

        if hepai_type is None or tingpai_type is None:
            # 尝试查找包含 "Qingque13" 的所有类型
            qingque_types = []
            try:
                if hasattr(asm, 'GetTypes'):
                    qingque_types = [t.FullName for t in asm.GetTypes() 
                                   if t.FullName and "Qingque13" in t.FullName]
            except Exception:
                pass
            
            asm_name = asm.FullName if hasattr(asm, 'FullName') else str(asm)
            error_msg = (
                f"已加载程序集 '{asm_name}'，"
                f"但未找到类型 'Qingque13.Qingque13Hepai' 或 'Qingque13.Qingque13Tingpai'。\n"
                f"程序集中包含 'Qingque13' 的类型: {qingque_types[:10] if qingque_types else '无'}"
            )
            raise RuntimeError(error_msg)

        # pythonnet 中，System.Type 需要通过反射调用静态方法
        # 我们保存类型对象，然后在调用时使用 GetMethod + Invoke
        Qingque13Hepai = hepai_type
        Qingque13Tingpai = tingpai_type
        print(f"[DEBUG] 成功加载类型: Qingque13.Qingque13Hepai, Qingque13.Qingque13Tingpai")
        
        # 加载 fan_cache.json（必需，否则评分会失败）
        try:
            qingque_scoring_type = asm.GetType("Qingque13.QingqueScoring")
            if qingque_scoring_type is not None:
                # 获取 fan_cache.json 的路径（与 DLL 同目录）
                base_dir = Path(__file__).resolve().parent
                fan_cache_path = base_dir / "Qingque13Calc" / "fan_cache.json"
                
                if fan_cache_path.is_file():
                    # 调用静态方法 LoadFanCacheFromFile
                    load_method = qingque_scoring_type.GetMethod(
                        "LoadFanCacheFromFile",
                        BindingFlags.Public | BindingFlags.Static
                    )
                    if load_method is not None:
                        load_method.Invoke(None, [str(fan_cache_path)])
                        print(f"[DEBUG] 成功加载 fan_cache.json: {fan_cache_path}")
                    else:
                        print(f"[WARNING] 未找到 LoadFanCacheFromFile 方法，fan_cache.json 可能无法加载")
                else:
                    print(f"[WARNING] fan_cache.json 未找到: {fan_cache_path}")
        except Exception as cache_exc:
            print(f"[WARNING] 加载 fan_cache.json 时出错: {cache_exc}")

    except Exception as exc:
        raise RuntimeError(
            "已通过 pythonnet 加载程序集，但未能解析 Qingque13.Qingque13Hepai / Qingque13.Qingque13Tingpai，"
            "请检查它们是否为 public 且在已加载的程序集内。"
        ) from exc
    _qingque13_loaded = True


def call_hepai_check(
    hand_list: list[int],
    tiles_combination: list[str],
    way_to_hepai: list[str],
    get_tile: int,
    debug: bool = False,
) -> tuple[float, list[str]]:
    """
    调用 C# Qingque13Hepai.HepaiCheck 静态方法
    
    Args:
        hand_list: 手牌列表
        tiles_combination: 明刻/明杠/顺子组合列表
        way_to_hepai: 和牌方式列表
        get_tile: 和牌牌编号
        debug: 是否开启调试日志
    Returns:
        (fan_score, fan_names) -> (番数, 中文番名列表)
    """
    _ensure_qingque13_loaded()
    
    if Qingque13Hepai is None:
        raise RuntimeError("Qingque13Hepai 类型未加载")
    
    # 获取静态方法
    method = Qingque13Hepai.GetMethod(
        "HepaiCheck",
        BindingFlags.Public | BindingFlags.Static
    )
    if method is None:
        raise RuntimeError("未找到静态方法 HepaiCheck")
    
    # 将 Python 列表转换为 .NET List（逐个添加元素）
    hand_list_net = List[int]()
    for item in hand_list:
        hand_list_net.Add(item)
    
    tiles_combination_net = List[str]()
    for item in tiles_combination:
        tiles_combination_net.Add(item)
    
    way_to_hepai_net = List[str]()
    for item in way_to_hepai:
        way_to_hepai_net.Add(item)
    
    # 调用静态方法
    get_tile_net = Int32(get_tile)
    debug_net = Boolean(debug)
    
    result = method.Invoke(None, [hand_list_net, tiles_combination_net, way_to_hepai_net, get_tile_net, debug_net])

    # 转换返回结果
    fan_score = float(result.Item1)
    fan_names = list(result.Item2)
    return fan_score, fan_names


def call_tingpai_check(
    hand_tile_list: list[int],
    combination_list: list[str],
    debug: bool = False,
) -> set[int]:
    """
    调用 C# Qingque13Tingpai.TingpaiCheck 静态方法
    
    Args:
        hand_tile_list: 手牌列表
        combination_list: 已完成组合列表
        debug: 是否开启调试日志
    Returns:
        等待牌集合
    """
    _ensure_qingque13_loaded()
    
    if Qingque13Tingpai is None:
        raise RuntimeError("Qingque13Tingpai 类型未加载")
    
    # 获取静态方法
    method = Qingque13Tingpai.GetMethod(
        "TingpaiCheck",
        BindingFlags.Public | BindingFlags.Static
    )
    if method is None:
        raise RuntimeError("未找到静态方法 TingpaiCheck")
    
    # 将 Python 列表转换为 .NET List（逐个添加元素）
    hand_tile_list_net = List[int]()
    for item in hand_tile_list:
        hand_tile_list_net.Add(item)
    
    combination_list_net = List[str]()
    for item in combination_list:
        combination_list_net.Add(item)
    
    # 调用静态方法
    debug_net = Boolean(debug)
    
    waiting_tiles_cs = method.Invoke(None, [hand_tile_list_net, combination_list_net, debug_net])
    return set(waiting_tiles_cs)


def call_get_base_point(fan: float) -> int:
    """
    调用 C# Qingque13Hepai.GetBasePoint 静态方法
    
    Args:
        fan: 番数
    Returns:
        基础分数
    """
    _ensure_qingque13_loaded()
    
    if Qingque13Hepai is None:
        raise RuntimeError("Qingque13Hepai 类型未加载")
    
    # 获取静态方法
    method = Qingque13Hepai.GetMethod(
        "GetBasePoint",
        BindingFlags.Public | BindingFlags.Static
    )
    if method is None:
        raise RuntimeError("未找到静态方法 GetBasePoint")
    
    # 调用静态方法
    result = method.Invoke(None, [float(fan)])
    return int(result)

