# RC11.2–RC11.6 / 5.11–5.16 旧阶段续接与源代码核对

> 2026-10-14。基于 GitHub `LET013/XuanJianXianZu` 的 `main` 当前源码审计。RC11 是旧**任务阶段**代号，不修改 `mod.json` 的发行版本号。继承既有实现，不回退到历史 RC11 文件。本记录不等于 WorldBox 实机通过。

## 当前源码中已存在的闭环及证据

| 模块 | 实际源码路径 | 源码核对结论 |
| --- | --- | --- |
| 世谱与玄鉴纪年 | `Systems/History/XjChronology.cs`、`XjCenturyAnnalsBuilder.cs`、`XjCenturyAnnalsStore.cs` | 已用持久化绝对世界年做玄鉴历基准、按完整百年分卷；已有旧档日历与旧世谱修复分支；旧存档/三千年地图仍须实机导入检验 |
| 成胎概率 | `Systems/HighRealm/XjDaoTaiAscensionSystem.cs` | 当前为基础 10%、权柄加成后至多 30%；无天霞前置狙击。RC11.2 临时 20% 被 RC11.3 后续规则替代，不应恢复 |
| 阴阳与群法 | `Systems/Actor/XjActorRuntimeStatProjection.cs`、`Systems/Combat/XjCombatDamageResolver.cs`、`XjTaiYinNonAggressionPolicy.cs`、`Data/HighRealm/XjJinDanDaoSpellCatalog.cs` | 已确认太阳在 `updateStats` 最终战斗面板应用 `ApplyTaiYangCombatStatScalar`（固定 ×3，不乘寿元、道慧、命数与模型尺寸），统一伤害结算器再以 `taiYangAlwaysHits` 令闪避率为零；太阴不争保护、低血闪避、十二炁/并古组合技有对应规则。仍须实机验证最终倍率和战斗调用链 |
| 并火道慧 | `Systems/Aptitude/XjDaoHuiPolicy.cs`、`Systems/HighRealm/XjHighRealmDaoStateService.cs` | 并火的有效道慧惩罚及高境修持调用有源码证据；没有运行百年平衡样本 |
| 古今释修高境 | `Systems/Shi/XjShiHighRealmSystem.cs`、`XjShiDomainState.cs`、`XjAncientShiTempleSystem.cs` | 已有金地、法相、世尊路径、失败/冷却、古释寺庙与旧档回填逻辑；未做实机跨代可达性统计 |
| 人丹与五神通 | `Systems/HighRealm/XjRenDan.cs`、`XjRenDan.Plans.cs` | 第 4/5 神通窗口各自独立 50% 计划判定；受目标家族、境界、占用与时限限制；仍须高人口运行以统计成功/截走/掺假分支 |
| 真灵与死亡 | `Systems/Shi/XjShiDeathLifecycle.cs`、`XjShiAnnouncementSystem.cs`、`History/Books/XjThreeBookWriter.Shi.cs` | 已有归返、俱灭与三书记录；本轮修复高位终局重复公告、主动转世重复公告、技术移除误发公告 |
| 龙属修炼 | `Systems/LongShu/XjLongShuSystem.cs`、`XjLongShuDongTianSystem.cs`、`Systems/Cultivation/XjCultivationLocalCore.cs` | 有龙属培养倍率和实际清修乘区调用、洞天状态机；需世界载档检查龙属依然存活与修炼可达 |
| 果位意象 | `Systems/HighRealm/XjGuoWeiImageStateService.cs` | 读写持久字段 `XjGuoWeiImageState`、历史字段及运行态缓存；旧档兼容存在，但尚未提供实机互夺权柄的演算录像 |
| 排行榜释修筛选 | `UI/Rank/XjRankWindow.Filters.cs`、`XjRankCardView.cs` | 本轮修正残留特质、仙修等效境界与释修真实道统误混，以及姓名境界重复问题；释修图标资源文件现存于 `GameResources/trait/` |

## 本轮实际写入的代码

- `UI/Rank/XjRankWindow.Filters.cs`：读取 `ShiTradition` 与 `ShiRealm` 的持久化事实来生成/命中释修筛选；不把为战斗所作的仙修等效境界当作释修境界；原有仙修、百艺筛选保留；使用轻量查询避免成千上万角色反复构建状态大快照。
- `UI/Rank/XjRankCardView.cs`：只在姓名尾部剥离六种释修境界后缀，不改变全名与存档。
- `Systems/Shi/XjShiDeathLifecycle.cs`：高位俱灭只播报一次；主动摩诃转世保留专用公告，不再重复“真灵归土”；技术性移除不投递世界播报；三书与依附关系事务不删改。

## 纯源码核对（不是运行门禁）

1. GitHub 源码读取、修改锚点唯一性、回读提交内容：通过。
2. 释修身份源、筛选条件先后顺序、原有仙修筛选入口保留：静态检查通过。
3. 姓名后缀剥离 6 个样例：算法核对通过。
4. 真灵播报（高境终局/低境终局/技术性移除/自愿转世/被迫归返/不归返/古释终局）7 类分支：条件表核对通过。
4a. 从 GitHub 回读的 3 个修改后 C# 文件，使用跳过注释/字符串的词法括号配对检查：三文件均通过；**这不是 Roslyn 编译或实机验证**。
5. **未执行：** MSBuild/Roslyn 编译、WorldBox 115 实机加载、真实旧存档读写、千年演化、战斗伤害统计、UI 点击与高人口帧率。
6. **未操作：** 不修改 `mod.json` 版本，不提供伪造发行包，不对真实玩家/世界做破坏性测试。

## 实机验收时应特别观察

- 旧地图的第一卷世谱必须从实际玄鉴起录年起算，不重复造出几十卷虚假的旧世纪。
- 在世的古释/今释和转换身份后的角色，筛选图标、境界列、姓名列完全一致；旧残留特质不得误入旧道统或仙修境界筛选。
- 摩诃主动转世、高境真灵被斩、普通今释归土、技术性角色移除各做一次独立场景，检验全世界公告次数与三书记录。
- 太阳 `ApplyTaiYangCombatStatScalar ×3` 与 `taiYangAlwaysHits` 分别已有明确实现；仍必须通过实机前后属性对比和实际命中统计，确认数值重建仅结算一次，且全来源伤害调用链没有重复乘区。太阴低血闪避/不主动攻击、道胎越境压制也需同步实机核对。
- 人丹第 4/5 独立窗口、同道途异族筑基、目标被截走/掺假/无目标、五十年到期，以及死亡/晋境时计划解除与副作用。
- 龙属洞天清修速度/死亡后清理、古释世尊/今释九世可达、金地/果位意象旧档还原。

## 范围与下一步

这是一轮**对已有 RC11 功能的集成审计与真实断链修复**。多数机制在最新主干已经存在，不能按旧阶段任务再次复制一套；上表中仍未实机验证的项目暂不作为正式发行 PASS，也不得声称所有 5.11–5.16 运行闭环已验收。
