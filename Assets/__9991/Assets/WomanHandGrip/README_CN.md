# Woman Hand Grip：掌根变形修复版

本包用于本次提供的 SK_Woman 和 SK_Woman_left。包含修复后的左右手 FBX、更新的动作组件与替换菜单。

## 安装

1. 停止 Play，先保存场景。
2. 解压 ZIP，打开里面的 WomanHandGrip 文件夹。
3. 找到项目里上次导入的 WomanHandGrip 文件夹（你的截图中在 Sources 下）。将新版文件复制到这个旧文件夹内，覆盖同名的 WomanHandGrip.cs 和 Editor/WomanHandGripEditor.cs，并加入 Models 文件夹。保留项目现有脚本 .meta，不要同时保留两套同名脚本。
4. 返回 Unity，等待编译。若 Console 有编译错误，先处理第一条错误，不要继续替换。
5. 在 Hierarchy 中 Ctrl 多选两只原手模型：例如 SK_Woman_white_left 和 SK_Woman_white。不要选择 Left Controller / Right Controller / XR Origin。
6. 顶部菜单 Tools > VR Hands > Replace Selected With Repaired Models。
7. 会创建 Left Hand Model Repaired 和 Right Hand Model Repaired，复制原手的父对象、局部位置、旋转、缩放及材质，自动配置抓握。原手模型只会停用，保留作为备份。
8. 保存场景。Play 后单独选择一只新手，勾选 Manual Preview，拖动 Preview Curl 从 0 到 1。确认张开、半握、握紧后取消 Manual Preview，再试手柄 Grip。

原模型位置被复制，但不同 FBX 的 Unity 导入处理可能造成手腕朝向/位置偏差。若出现这种情况，只调整新手模型根对象的 Transform，使其对齐原模型；不要移动单根手指骨骼。原模型可作为对照，查看完后继续停用，避免重叠显示。

## 本次修复内容

- 重新生成两份 FBX，增加明确的 palm_root_l / palm_root_r 蒙皮骨骼，掌根不再依赖特殊 FBX Root 节点处理。
- 补回原 FBX 导入 Blender 时丢失的掌根权重，归一化每个顶点，保留最多四个影响骨骼；保留原模型形状和 UV。
- 烘焙模型旋转/缩放，避免原始左手负缩放参与蒙皮计算。手指仍使用原来的 15 个骨骼名称。
- 新手的 Skinned Mesh Renderer 使用 Bone4；组件运行时也明确启用四骨骼蒙皮。
- 修复 Inspector 多选时仅显示“Multi-object editing not supported”的问题。
- 抓取逻辑和 XR Controller 保留；这套组件只控制手的外观动作。

## 验证与边界

两份修复 FBX 均重新导入 Blender 检查：16 根有效骨骼，2589 个顶点，无无权重顶点，所有顶点权重和约为 1。修改前后静止顶点世界位置最大差异小于 0.00000005 米。已检查重新导入模型的张开、半握、握紧六种组合；预览没有再次出现掌根拉裂。

这说明修复模型自身的蒙皮在上述检查中成立，不等于已证明截图的唯一原因。原场景的骨骼引用、其他动画脚本、Unity 导入设置也可能影响结果。本环境没有 Unity Editor，尚未完成 Unity 编译、现场渲染或头显验证。

如果新手仍破面，请单独选择出问题的那只手，展开内部带 Skinned Mesh Renderer 的网格对象，提供该组件的 Inspector，以及 Preview Curl 为 0 / 0.5 / 1 的画面。不要同时选择两只手，这会隐藏部分单对象信息。

## 回退

停止 Play，停用新生成的 Repaired 手对象，重新启用原手模型即可。菜单没有删除原模型或覆盖原 FBX。替换操作也支持 Undo。

## 操作

保持你现有的模拟器设置：右手 G，左手 Shift+G；真实手柄分别读取 LeftHand/RightHand 的 Grip。Manual Preview 开启期间会忽略 Grip。

Max Curl 可降低握紧程度，但仅降低程度并不能修复错误的蒙皮。精确夹住工牌、根据物体形状自动弯曲手指不属于本包功能。

API 参考：https://docs.unity3d.com/cn/6000.0/ScriptReference/SkinQuality.html
