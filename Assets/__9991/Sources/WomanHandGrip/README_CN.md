# Woman Hand Grip — 左右手握拳动画

适用：本次提供的 SK_Woman.fbx（右手）和 SK_Woman_left.fbx（左手），Unity 6、XR Interaction Toolkit 3.x。

## 安装（保留你现在的手模型）

1. 停止 Unity 的 Play。
2. 双击压缩包内的 WomanHandGrip.unitypackage，选择 Import。
3. 在 Hierarchy 中按住 Ctrl，选中 Left Controller 下的 **Left Hand Model** 和 Right Controller 下的 **Right Hand Model**。
4. 顶部菜单选择 **Tools > VR Hands > Set Up Selected Hand Models**。
5. 提示已配置 2 只手后保存场景。两个模型上各会出现 **Woman Hand Grip** 组件，Hand 自动设成 Left / Right。
6. Play 后测试握持：组件分别读取各自手柄的 Grip 值，按下时弯曲手指、松开时张开。

仍保持原来的手模型、材质、手腕位置、射线、抓取组件和输入绑定。组件只改变 15 根手指骨骼的 localRotation。

### 手动安装备用

也可以在两只 Hand Model 上分别 Add Component > Woman Hand Grip。左手 Hand 选 Left，右手选 Right；然后点击 **Configure / 识别骨骼并设置握拳**。
不要把它加到整个 XR Origin、Left Controller 或 Right Controller 上。它只应该管理模型的骨骼。

## 在电脑和头显里测试

- 你的当前 XR Interaction Simulator 配置：右手 G，左手 Shift + G；Game 窗口需获得焦点。
- 真实 VR 控制器：按各自手柄的侧面握持键（Grip）。键盘快捷键不会成为头显上的按键。
- **Editor Keyboard Fallback 默认关闭**。保持关闭可让手型和现有模拟器的 Grip 一致。只有调试输入时才勾选；它只作用于 Unity 编辑器，不会修复其他物体的抓取逻辑。
- 如你的动作资产对 Grip 有特殊绑定，可把对应的 float Input Action 拖到 **Grip Action**。该 Action 应由项目的 Input Action Manager 启用。通常保持空白即可。

## 只测试动画

Play 后选中 Hand Model：

1. 勾选 **Manual Preview**。
2. 拖动 **Preview Curl**：0 = 张开，0.5 = 半握，1 = 握紧。
3. 测完取消 Manual Preview，恢复手柄驱动。

Inspector 下方显示 Grip Input 和 Finger Curl。
- Grip Input 始终 0，但预览能握拳：检查模拟器当前动作目标/Left Device Actions，或自定义 Input Action 绑定。
- Grip Input 变化，但没动画：检查是否把组件挂到了正确模型，Console 是否提示缺少骨骼，以及是否有其他脚本在写手指旋转。
- 两只手都没反应：先确认菜单提示成功配置，且 Console 没有编译错误。

## 调整手型

- **Maximum Curl**：默认 1。抓薄卡片时可试 0.65–0.8，让手指弯曲小一些。此值对整只手生效。
- **Response Speed**：默认 16，越大越快。
- **Reverse Palm Direction**：原始模型通常不需要。只有额外镜像/改骨骼后弯曲方向错误时使用；停止 Play，调整后重新 Configure。

## 导出真正的 .anim 文件（可选）

停止 Play，选中一只 Hand Model，点击组件下方的 **Export .anim Clips / 导出动画片段**。

将在 Assets/WomanHandGrip/Left_Animations 或 Right_Animations 文件夹中生成：

- Open：张开姿势。
- Grip：握紧姿势。
- Close：0.25 秒张开到握紧。
- Release：0.25 秒握紧到张开。

另一只手同样操作。重复导出会新建文件夹，避免覆盖原动画。
动画绑定路径相对于你选择的 Hand Model。它们可用于你自己后续制作的 Animator Controller。
**当前组件已经直接驱动动画，不需要额外 Animator Controller。** 组件启用期间会暂时关闭该模型内部的 Animator，避免静止的 Take 001 覆盖手指；禁用组件时恢复 Animator 的启用状态。
如改用自己的 Animator Controller，请先停用/移除 Woman Hand Grip，避免两个系统同时写骨骼。

## 适用范围与验证

这是一套基于 Grip 数值的预设握拳动画，不是裸手追踪，也不是会自动贴合任意物体表面的手指碰撞系统。它不更改机盖、工作牌的选择/抓取逻辑。卡片仍可被抓取而手指没有精确夹在卡片两侧；针对某件物品的精确捏取需要单独的抓取姿势。

已检查：两份模型各有 15 根手指骨骼；右手原 Take 001 只有一帧且曲线不变化，左手没有动作。已用这两份模型检查张开、半握和握紧变形，包括镜像转换。Blender 导入器丢弃了原 FBX 根节点的蒙皮贡献，预览重建了这部分权重；没有修改交付给你的原始 FBX。

此环境没有 Unity Editor，尚未完成 Unity 编译、你的场景或真实头显实测。安装后请先用 Manual Preview 检查两只手，再测试 Grip Input。预览图使用临时中性材质；Unity 中继续使用你原来的材质。

## API 参考

- InputAction：https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/api/UnityEngine.InputSystem.InputAction.html
- AnimationUtility.SetEditorCurve：https://docs.unity3d.com/ScriptReference/AnimationUtility.SetEditorCurve.html
- AnimationClip.EnsureQuaternionContinuity：https://docs.unity3d.com/ScriptReference/AnimationClip.EnsureQuaternionContinuity.html
