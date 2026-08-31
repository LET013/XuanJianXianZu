# 玄鉴运行架构

> **当前发布口径（1.0 RC）**：本文前半部分按版本保留历史演进记录，其中 0.8.x/早期 1.0 章节出现的 archive 103/104 是当时规划，不再是当前存档契约。**当前代码权威值为 archive version 123，MinimumSupportedV1ArchiveVersion = 116**，以 `Systems/Archive/XjWorldSchemaGuard.cs` 为唯一准据。

## 模块边界

- `Core`：世界生命周期、统一检测门禁、队列与预算；不实现具体玩法。
- `Systems`：按领域保存玩法状态与规则。宗门、家族、洞天、阴司、四艺彼此不得直接启动全世界扫描。
- `Runtime`：只消费已有索引和队列；不得在渲染帧中执行全量角色或城镇循环。
- `UI`：只读取快照、渲染和处理点击；不得修复存档或触发玩法结算。
- `Patches`：仅为 WorldBox 回调适配入口；复杂规则必须转交到领域系统。

## 调度约定

- 世界级年度任务必须经 `XjDetectionGate.TryBeginAnnualJob` 去重。
- 角色级维护只通过 `XjScheduler` 的有界队列执行；新角色、修士、死亡角色分别维护索引，禁止临时全量扫描。
- 城镇级任务优先使用城市/宗门索引。只有读档修复、世界迁移等冷路径可以扫描 `World.world.cities`，且必须有年度门禁或分帧预算。
- `lastYear` 只保留为实体玩法冷却、历史记录或存档迁移状态；不能被用作新的独立世界扫描调度器。

## 宗门归属

宗门的唯一业务主键为 `SectId`：

- `CityId -> SectId`
- `ActorId -> SectId`
- `FamilyId -> SectId`
- `SectId -> 仓库、峰脉、大阵、洞天、历史`

`KingdomId` 仅作为 WorldBox 原生显示、战争动画和兼容层，不可再作为宗门玩法的唯一归属来源。

## 性能目标

- 常驻帧路径只做输入、轻量状态和已知 UI 更新。
- 年度重活拆入预算队列，禁止同一世界年重复启动。
- 每项新玩法必须先定义：触发索引、预算、门禁 key、存档状态和 UI 快照来源。
- 百年世谱是历史压缩归档，不是第二套实时世界历史；生成入口只能位于年度世界通道，
  且必须使用 `XjDetectionGate.AnnualCenturyAnnals`。
- 正典标注是静态设定元数据，不参与战斗、修炼、掉落、AI 和世界存档。

## 运行态归属

- `XjActorRegistry` 只保存已知角色引用；角色死亡或失效必须通过
  `XjScheduler.ForgetActorRuntimeState` 释放关联缓存。
- `XjDetectionGate` 只负责世界级年度去重与固定节奏；实体冷却、读档游标和
  历史年份仍由各自领域存档持有，不能为了统一形式删除。
- `XjRuntimeCadence` 只分配渲染帧预算，不承载玩法规则。高倍速下的逻辑步必须
  合并为单次队列消费，不能在同一渲染帧重复全量运行。
- 可关闭任务、符箓批次、炼丹任务等运行态必须有归档或保留上限；开放任务才可
  长期留在热路径。
- 城镇、国家和家族查询优先通过 `XjWorldLookupIndex` 或领域索引；只有读档迁移
  与显式修复允许扫描 `World.world` 全表。

### 原生对象引用生命周期

- 跨帧队列、年度索引、排行榜/百科缓存与领域增量索引默认只保存稳定 ID 或不可变 DTO；不得为了省一次解析而长期建立第二套 `Actor` / `City` / `Kingdom` / `Building` / `Army` 对象缓存。
- 原生对象引用只允许三类例外：
  1. `XjActorRegistry`、`XjWorldLookupIndex` 等承担“ID -> 当前世界实例”职责的**唯一解析器缓存**，且必须验证世界身份并在换档/清档时清空；
  2. bootstrap、原生转移保护、存档修复等**有明确结束条件的短事务快照**，完成后必须立即释放；
  3. 业务操作本身依赖对象身份且改成 ID 会重新引入局部扫描的有界缓存，此类例外必须在代码旁说明失效条件。
- 跨帧工作若需要原生对象，入队时保存 ID，消费边界再经唯一解析器获取当前实例；第三方模组替换实例但沿用同一 ID 时，不得错误复用旧实例派生缓存。
- 一项性能缓存若需要新增多套 invalidation、读档修复或常驻自愈才能保持正确，而实测收益不足以覆盖维护成本，应直接关闭或删除，不因“已经实现”而继续保留。

### 热路径禁止项

- `Actor` 年度/属性、战斗命中受击、移动与原生高频 Harmony 入口只允许 O(1) 状态判断、索引读写和有界入队；禁止直接扫描 `World.world.units/cities/kingdoms/buildings`。
- 战斗与角色高频入口禁止构建 Codex/排行榜/历史快照，禁止写长篇历史，禁止启动关系、战争、宗门治理等世界级规划。
- UI 渲染只消费 ViewModel/Snapshot；禁止通过 `World.world.*` 全表构建页面，禁止为显示触发业务修复。
- 上述规则由 `tools/xj_architecture_guard.py` 做发布前静态守卫；现有明确例外只能在守卫脚本中以窄路径登记，禁止使用目录级万能白名单。

## 文件组织

- 文件数量不等于运行次数。规则、状态、归档、运行车道和 UI 应保持分离，不能为
  减少文件数而重建巨型系统类。
- 允许合并的对象仅限同一领域内没有独立生命周期的微型 `partial`/适配文件。
- 新增功能按以下顺序落位：`Data` 定义持久模型，`Systems` 保存规则和索引，
  `Runtime/Core` 负责预算调度，`UI` 只消费快照，`Patches` 只做原生入口适配。
- 删除候选必须先满足“无注册入口、无调度入口、无存档字段引用”；不能只根据
  文件名或当前 UI 未显示就删除。

## 0.8.2 百年世谱与正典标注

- 百年世谱数据位于 `Data/History/XjCenturyAnnalsData.cs`，内存与导入导出位于
  `Systems/History/XjCenturyAnnalsStore.cs`，生成规则位于
  `Systems/History/XjCenturyAnnalsBuilder.cs`。
- 百年世谱只读取家族台账、宗门席位/治理、世界历史窗口和有界增量账本；不得为了补
  展示字段扫描全部 Actor。
- `XjWorldArchiveData.Version` 在 0.8.2 升级为 103，1.0 正式版升级为 104。
  `XjWorldSchemaGuard.MinimumSupportedV1ArchiveVersion` 保持 103，因此 alpha.4/0.8.2 后统一归档可安全读取；
  缺失的 `LastMandateYear` 按 0 迁移，下一次保存写为 104。更早归档仍拒绝半迁移。
- Codex UI 通过 `XjCodexSnapshot.CenturyAnnals` 读取百年世谱；UI 不调用 Builder、
  不补算、不修复归档。
- 正典标注统一入口位于 `Data/Lore/XjCanonMetadata.cs`。未人工登记的条目统一返回
  `PendingReview`，UI 显示【待校核】。
## 1.0 世家春秋（第一批）

- 世家志业仅在百年世谱结算时评估，复用家族聚合、功法仓库和既有家族阶段；不新增年度或帧级扫描。
- 志业仍只使用“求法、育府、求金、复振”四个既有内部值，状态附着在 `XjCenturyFamilyStageStateRecord`，不驱动角色 AI；仙鉴对外将“育府／求金”表述为道途中立的“育真人／求真君”。
- 宗门恩债由现有家族席位的 `SupplyDebt`、`VoiceScore`、`PrivilegeHeat` 等字段直接推导，不建立第二套关系账本。
- 道统兴替通过相邻两卷 `DaoSummaries` 比较得出“大兴、衰退、断传、复传”，不建立独立道统状态机。


## 1.0 世家春秋（第四批：世家传承与族议）

- 家族扶持的唯一持久状态继续附着于 `XjCenturyFamilyStageStateRecord`：只保存一名
  `SupportedActorId`、扶持方向、立举年份、最近族议年份和上任家主观察值；不得创建候选池、
  培养点、进度条或第二套家族资源账本。
- 家族扶持仍只通过 `XjAnnualWorldRuntimeLane` 的 `AnnualFamilySupport` 串行阶段落账，但普通族议只在五年主批次进入；
  每个家族按稳定ID固定为5年或10年扶持周期。`xjzz5/6` 后辈在资质真正写入时只登记一次紧急家族信号，下一次年度世界车道只处理对应家族并可绕过普通扶持冷却；没有 `Update`、`FixedUpdate`、寻路任务或新角色 AI。
- 所举人选只在引用缺失、死亡、离族、境界不再匹配、志业转换或出现 `xjzz5/6` 紧急后辈时重选。普通候选比较优先资质，再比较原有修为/道慧/命数等指标；高境长辈亲自改选、点拨与布局统一进入50年级次级AI。
- 紫府金丹一侧的族议只调用现有采气法、五品功法、求金法和家族法宝接口。求金法必须满足五门仙基、五部
  真实五品功法且角色尚未拥有求金法；不得借族议绕过功法—仙基—求金主链。服气养性一侧只缩短已经存在的神妙归身、神妙圆满或金性温养期限并增加少量道慧，不生成紫府资源。
- 志业完成在百年世谱中正式结算。年度入口只停止继续拨付并保留本世纪所举之人，避免完成
  育府或求金后在世谱生成前丢失关键承志者。
- 家主使用家族台账既有代表人物作为最低成本继任来源，只在旧家主失效时更替；族中支柱、
  镇族重宝均从当前成员与真实仓库派生，不保存第二份权威状态。
- 家族详情与百年世谱只读取 Codex 快照。仓库投影使用瞬时只读视图，UI 不触发族议、不修改
  志业、不补发资源。
- 百年世谱内部 Schema 为 12；`BaseWorldYear` 是玄鉴历唯一纪元基准：起录当年为第1年，第1卷覆盖玄鉴历1–100年。世界事件里程碑、世谱卷次和玩家可见纪年统一由该基准换算；冷却、持续时间和真实发生时间戳仍保存绝对世界年。Schema 10/11 的绝对百年卷在迁移时按新基准校验，不对齐的假卷舍弃，并由留存世界史与账本在真正满百年后重建。正常换算 O(1)，仅迁移允许扫描有界卷宗与账本。旧档缺失的扶持字段安全归零，不自动补发资源。年度幂等由
  `AnnualFamilySupport` 串行门禁、5年主批次/紧急信号与 `LastSupportYear` 双重保证。


## HF4 性能收束（低频维护口径）

- 宗门讲法：真人/紫府10年一次，真君/金丹50年一次；真君讲法单次奖励显著高于真人，避免把低频调整变成隐性削弱。
- 宗门任务保持5年一次，并按五年窗口提高资源与贡献产出。秘境训练与秘境次级维护改为3年批次，进度/道慧/资源按单位年份折算；已经开始的秘境工程仍按原 `DueYear` 推进，不因降频改变建造完成年份。
- 上修扶金、金丹赐予、命数做局、高境执棋与家族高境亲自扶持统一收束到50年级次级AI；普通家族族议保持5/10年，`xjzz5/6` 走事件信号。
- 三书改为“重大事件发生源即时记史 + 五年世界摘要/资源里程碑 + 十年个人社交观察 + 百年世谱”的分层结构，不再依赖年度重复观察来重建大事。
- 符箓计划/采材4年、阵法采材3年、紫府装备维护4年、稳定高境维护5年、失落法宝发现5年相位检查；批处理类产出按窗口折算，避免因减少检测次数改变长期经济总量。

## 1.0 世家春秋（第五批：宗门中的世家）

- 掌宗世家、宗门柱石和离心家族只从宗主、家族聚合与既有席位字段派生；不保存派系、支持率或第二套政治标签。
- 宗主继任只在原宗主失效时进入候选比较。候选差距明显时直接继任；同境界、不同家族且真元与话语接近时，执行一次确定性争位判定，并把结果写回既有话语权、贡献和供养债务。
- 宗主法旨只保存 `LastMandateYear`，每二十年最多真实执行一次。固定优先级为赐法扶族、催缴供奉、敲打强族、授意寻仇；条件不成立时不制造事件、不进入冷却。
- 赐法扶族复用宗门采气法、功法与求金法借法入口；授意寻仇只提高既有稀疏宗门敌对，不新增寻敌、追杀或角色 AI。
- 第五批继续复用 `XjSectGovernanceRuntimeLane` 的年度治理队列。家族阶段、席位、家族聚合和稀疏敌对在年度投影中各读取一次，不新增宗门扫描器。

## 1.0 世家春秋（第六批：可见性与正式发布）

- 宗门 Codex 在原卡片内显示掌宗世家、宗门柱石、离心家族和最近法旨年份；所有政治标签均由快照派生，UI 不反向修改世界状态。
- 百年世谱的家族阶段说明改为叙事式归档，并从已有宗门事件账本压缩出最多四段冲突因果线；不建立第二套历史或实时因果图。
- 世界归档版本为 104，最低兼容版本为 103；宗门领域 Schema 为 3。alpha.4 旧档缺失的法旨年份安全归零，不补发法旨、不重演继任。
- 正式发布仍以统一年度入口、稀疏敌对、只读 Codex 快照和有界百年账本为性能边界。

## 0.9.0 发布收口：统一检测与历史分类图标

- 世界级年度任务统一进入 `XjAnnualWorldRuntimeLane`。车道按“阶段数上限 + 墙钟上限”
  在一次 fast cadence 内连续跳过空阶段并推进有资格任务；高倍速跨年时仍按年份顺序追补，
  不允许无预算地在单个渲染帧内连跑整套年度系统。
- `XjUnifiedDetectionPlan` 每个待处理世界年只采集一次轻量资格快照。快照只读取修士、
  宗门、洞天、任务、恩怨和百年卷等既有索引/计数，不扫描 `World.world.units`、
  `World.world.cities` 或 `World.world.kingdoms`。
- `XjDetectionGate` 集中定义三类节奏：
  1. `XjDetectionJob`：世界级年度门禁与最低间隔；
  2. `XjEntityDetectionJob`：角色、家族、城镇等实体的确定性错峰年槽；
  3. `XjRuntimeDetectionJob`：增长、快速队列、归档提交、地形特效和索引清理的帧节奏。
- 业务冷却仍由领域存档持有。例如瓶颈每三年、法旨二十年、宗门战争阶段等属于玩法状态，
  不得为了“统一检测”移入全局门禁，也不得因此丢失存读档幂等性。
- 关闭的炼丹/百艺任务只保留十二年，统一三年清理一次，每轮各最多移除 512 条；开放任务
  继续使用 O(1) 索引，不扫描历史任务表。
- 城镇和国家 ID 查询统一使用 `XjWorldLookupIndex`。负查询缓存每个世界年清空一次，避免
  新建或替换实体在集合数量未变化时长期被旧 miss 隐藏；全表扫描仍只发生在首次建索引、
  集合数量变化或单个未知 ID 的首次缺失查询。
- 天下纪事九类总图标固定映射为：修行 `HistoryCultivation`、家族 `JiaZu`、宗门 `ZongMen`、
  传承 `ChuanCheng`、百艺 `HistoryCraft`、机缘 `JiYuan`、恩怨 `EnYuan`、生死 `ShengSi`、
  天下 `HistoryWorld`。筛选按钮、统一史册和原生世界历史镜像共用同一映射入口。

## 0.9.6.2 发布稳定性整合

- `SectId` 档案与 `XjSectAuthorityStore` 是宗门实体、领地和成员归属的唯一业务权威；`XjSectCultivatorCityIndex` 只维护“修士当前所在城市”的增量聚合，不再转发 `SectId -> Members` 或 `ActorId -> SectId`。角色/城镇字段只用于原生窗口、兼容迁移和投影校验。
- 发布不变量审计同时核对宗主、峰主、城镇与家族席位的SectId关系。只允许自动修复可从档案无歧义推出的镜像，不自动重建神通、果位、功法或高境身份。
- 项目禁止空catch。兼容/反射失败统一交给 `XjExceptionDiagnostics` 限频记录；热点逻辑仍不得用异常作为正常分支。
- FPS 显示只负责展示帧率，不开启深度性能采样。`PerformanceObservation` 才启用热点/语义/图形诊断；长局完整不变量审计属于最终验收显式入口，不再作为生产年度车道阶段。
- Codex外层只允许纵向滚动。长文本必须自动换行，卡片列数必须按可用宽度计算，禁止用横向滚动掩盖布局溢出。

## 0.9.6.3 单权威架构整合

### 宗门唯一权威

- `XjSectArchiveRecord` 保存宗门实体、领地、宗主和峰脉；`XjSectMemberArchiveRecord` 保存唯一成员归属、入门年份、职阶和峰位。
- `XjSectAuthorityStore` 是唯一运行期索引：`ActorId -> SectId`、`SectId -> Members`、`CityId -> SectId`。
- `XjSectCommands` 是唯一业务写入口；任何系统不得直接以 `actor.data`、`city.data` 反推或修改宗门状态。
- `XjSectProjection` 单向将权威状态写入原生角色和城镇字段。旧字段不得参与运行期业务判定；仅 `XjSectLegacyMigration` 可读取用于一次迁移，`XjSectProjection`/`XjSectAuthorityAudit` 只可为投影同步与一致性校验访问。
- 峰位属于宗门而非峰主：峰主调任、死亡或离宗只清空峰主，不删除峰位。

### 原生兼容边界

- Harmony、`System.Reflection`、`AccessTools` 和原生私有成员访问只能位于 `Interop/WorldBox`。
- `InterestingTrait.cs` 只调用 `XjModBootstrap`；补丁由 `XjWorldBoxPatchCatalog` 统一安装，补丁目录只接收原生事件，不建立第二套业务权威。
- 核心原生方法在启动时一次性验证；缺少核心能力时停止玄鉴运行逻辑，禁止静默进入不完整状态。

### 年度工作中心

- 宗门只保留 `XjSectRuntimeLane` 一条运行车道，统一消费事件、Dirty 投影、治理、招募与师徒维护。
- 年度世界车道只保留实际使用中的 `CollapseToLatest` 与 `BoundaryOnly`；逐年机会与累计产出由领域自身表达，不为未来策略预留空枚举。
- 高速追赶不得重复构建宗门投影、国家名称清理、世界快照和历史当前态维护。

### UI 基础组件

- `XjUiSafeText` 是玩家文本统一出口；内部ID、配置键和代码名不得直接渲染。
- `XjUiLayoutMetrics` 统一自动测高和自适应列数；可变文本禁止写死高度。
- `XjUiEntityCard3Line` 固定人物卡为姓名、核心数值、道途·境界三行。
- 页面只允许外层纵向滚动，禁止横向滚动掩盖布局溢出。

### 后续大型功能接入

- 新功能必须明确唯一持久状态、命令写入口、一个有界年度任务和只读 ViewModel。
- 原生事实与角色写入统一经过当前实际工作的 `XjInternalEventBus` / 领域命令入口；不得预建无人消费的第二事件总线。
- 未完成的大功能不得预注册运行任务、UI 或世界内容；只有完成数据/命令/年度任务/UI/迁移闭环后才接入正式模块目录。

## 0.9.6.7 模块化边界收口

- 启动入口统一为 `XjModBootstrap`，模块由 `XjFeatureModuleCatalog` 按稳定 ID、顺序和依赖初始化；模块可登记自己的世界清理回调。
- 后台、死亡终结和角色年度次级玩法分别进入 `XjBackgroundLaneRegistry`、`XjDeathLaneRegistry`、`XjAnnualActorExtensionRegistry`；新增功能不得再扩写固定 phase switch 或镜像 `HasPending` 总表达式。
- 世界归档版本升级为 123。新功能使用 `XjModuleArchiveRegistry` 的模块文档，不再向中央归档 DTO 添加字段；未知、高版本和导入失败文档均保留原始载荷。
- `Core` 与 `Systems` 不得直接依赖具体 UI。展示失效与 tooltip 元数据经 `XjPresentationHooks` 单向发布，由 `XjUiPresentationBridge` 在 UI 组合边界绑定。
- `Systems/Rank` 保存排行榜计算、排序与读取模型，`UI/Rank` 只负责控件和渲染；宗门地图图层归入 `UI/Map`。
- `Sect` 是宗门唯一生产业务域；`Systems/ZongMen` 已移除。历史 `ZongMen` 存档键只作为兼容 schema 保留，所有运行期变更必须经过 Sect 权威模型与 `XjSectCommands`。
- Harmony 只有 `XjWorldBoxPatchCatalog` 一个安装入口；所有包含 `[HarmonyPatch]` 的具体补丁类必须登记，架构审计会阻止漏装。
- 发布前必须执行 `tools/run_architecture_checks.cmd` 或 `.sh`。完整说明见 `docs/模块化边界与扩展指南_0.9.6.7.md`。

## 0.9.11.0 运行边界重整与高压削峰

### 单渲染帧总预算

- 所有非关键运行工作除自身车道的“条目数 + 墙钟”预算外，必须继续服从 `XjRuntimeFrameGovernor` 的**累计渲染帧预算**。禁止再以“每个子系统各自不超过 1ms”作为可叠加执行的理由。
- `Critical` 只保留会影响死亡事实、迁移正确性的刚性入口；年度核心、维护、后台按优先级逐级提前让出预算。预算耗尽时只能保留队列等待下一帧，不得丢弃 Exact 语义事务。
- 地形、归档、年度世界阶段和后台车道都要把超出预算的入口写入 `frameOverrun.*` 诊断，实机优化优先依据真实热点，而不是继续扩大统一扫描或无依据降低功能频率。

### 年度世界控制面

- `XjAnnualWorldRuntimeLane` 只负责年份排队、阶段推进、预算和追赶；领域调用继续由 `XjAnnualWorldCommandReducer` 归约。
- 原先一个阶段串行执行多套系统的组合已拆为单职责阶段：世界事件、冒险地、秘境、道途维护、不变量审计、故尊、国名、权柄、阴司、宗门、家族、三书、快照、百年世谱、释修归返、Codex、回归监控、内存维护均可在阶段之间让出当前帧。
- 某个领域自身仍是大循环时，阶段拆分只能限制“跨系统叠峰”，不能中断领域内部循环；这类系统应根据 `frameOverrun.annual-world.*` 继续改造成游标/队列，而不是在归约器里继续堆逻辑。

### 修炼体系模块边界

- 第三修炼体系必须通过 `XjAnnualCultivationPathRegistry` 注册 `Matches / Prepare / Progress / CombatTier`，不得再在 `XjSchedulerActorPipeline` 中增加 `if (IsXXX)` 专用分支。
- 释修已经迁入 `XjShiRuntimeComposition`；中央年度角色管线不再引用任何 `XjShi*` 类型。紫府金丹/服气养性暂保留既有主链，后续只有在能够证明事务边界完全等价时才继续迁移，禁止为了“形式统一”重写成熟主链。
- 世界载入/清档钩子使用 `XjWorldLifecycleRegistry`。释修与宗门均由各自 composition 注册生命周期，不再向 `XjInternalEventBus` 增加新的领域初始化调用。

### 高压事务不得在角色年度步骤内爆发

- 任何“一名角色年度结算 -> N 次真实死亡/生成/存档/历史写入”的逻辑不得同步完成。Exact 规则必须转成**持久语义债务 + 后台逐项消费**。
- 释修年度度化仍严格欠下每年 10 名非修士的真实死亡，但 `XjShiDuhuaRuntimeLane` 每次后台入口最多制造一次真实死亡；主动击杀仍额外计数且不抵扣债务。没有合法凡人目标时保留持久债务并退避 24 个渲染帧，候选搜索同时限制真实角色访问数与空城/城市边界等结构遍历数，禁止“无目标世界”后台热自旋。
- 释修真灵归返的年度控制面只登记到期真灵；`background.shi-return` 每次最多重塑一具肉身。失败项不在同帧高速重试，避免错误承载条件形成自旋。

### 世界局部净界与反射

- 旃檀林运行维护由释修模块注册的 `background.shi-sanctuary` 单独持有；角色年度 `EnforceActor`、世界载入回调和 UI/GodPower 注册都不得触发整域地形/领地维护。
- 旃檀林正常年度防御只查询局部角色，不再年度遍历 `World.world.buildings`。矿物采用原生生成入口拦截，完整建筑清理由“首次载入人口对账 / 首次放置 / 地形重建”这些稀有修复窗口兜底。
- 国家占领以 `City.addZone` 入口拒绝为实时权威；全地图区块剥离仅作为旧档/第三方旁路的**十年一次**低频自愈，不再每年遍历全部战略区块。
- 旃檀林不可避免的原生私有成员反射必须按 `Type` 缓存 `MethodInfo / FieldInfo / PropertyInfo`；禁止每年、每个区块重复 `GetMethod/GetField/GetProperty`。
- 能由生成入口阻断的问题，优先“入口阻断 + 稀有修复”，禁止为了理论完备增加常驻全世界扫描。
- 宗门洞天闭关建筑回退必须只查询角色所属城市的 `city.buildings`；禁止在一次角色缓存失效中多次遍历 `World.world.buildings`。当前实现用单次本地遍历按“已存目标 → 家宅 → 可居住建筑 → 任意有效建筑”确定回退点。

### UI 与运行态分离

- 排行榜虚拟列表由 `ScrollRect.onValueChanged` 驱动，禁止用 `Update()` 每帧计算可见区间；关闭按钮兼容修复降为 4Hz。
- 运行期本地化只在 `LocalizedTextManager` 实例变化或尚未注册时补录；已稳定状态不得每帧重新查询/写入 TraitAsset。
- UI 仍然只读取快照/只读模型；UI 打开、滚动、切页不得触发年度业务、历史补写或世界扫描。

### 清理所有权

- `XjScheduler.Clear()` 只清理 Scheduler 自己的队列、游标和车道状态。具体领域缓存必须由 `XjFeatureModuleCatalog` 的 `ClearRuntime` 或 `XjCacheRegistry` 的对应领域入口清理，禁止在 Scheduler 再复制一份清档 fan-out。
- 释修 runtime cache 由 `XjShiRuntimeComposition.ClearRuntime` 统一持有；宗门的 runtime lane、讲法、宗战、阵法占领/注册表与仓储状态均由 `XjSectRuntimeComposition.ClearRuntime` 持有。新模块不得把自己的缓存清理重新塞回 Scheduler 或 `XjCacheRegistry`。

### 当前仍保留的技术债务

- `XjScheduler`、`XjShiDomainState`、`XjManualRealmTraitReconciliation`、功法集合和历史存储仍是大文件；后续拆分应围绕“权威状态 / 命令 / 运行游标 / 投影”边界进行，禁止纯粹按行数机械切文件。
- `XjInternalEventBus` 是当前唯一实际使用的原生事实入口；新增跨领域通信必须先证明已有命令/写网关无法表达，不预留无人订阅的事件总线。
- `Data` 仍有少量反向依赖 `Systems` 的旧代码，属于层级倒置；只有当对应查询接口稳定后再迁移到 Rules/Ports，禁止本版本大面积移动命名空间造成存档与编译风险。
- `XjFamilySupportSystem` 已改为 `BeginYear/TickPending` 有状态家族游标：同一世界年的族议语义不丢失，但按条目数、墙钟与全局帧预算跨帧消费。旧的同步 `TickYear` 入口已删除，避免后续调用者绕过年度预算。
- 后台车道为避免高倍速叠峰，仅在全局帧预算前 72% 允许进入；若核心年度积压长期占满预算，`Exact` 后台语义（尤其释修度化/真灵归返）可能出现吞吐滞后而不是同帧爆发。当前选择“保留债务、延迟消费”而不是强行抢占帧预算；实机应同时观察债务长度与 `frameBudgetExhausted`，有证据后再设计保留配额。


## 1.0 RC 发布边界收口（工程审计）

- `Core -> UI` 的 ActorInfo 缓存清理已改由 `XjPresentationHooks` 单向端口发布；Core 不再知道 UI ReadModel 具体类型。
- `XjNativeScalarInterop`、`XjActorPresentationInterop`、`XjTerritoryInterop` 集中承接原生反射/私有成员兼容，领域与 UI 业务文件不再自行反射。
- 旃檀林 GodPower 注册、按钮、玩家提示迁入 `UI/Shi/XjZhantanlinPlacementUi`；`XjZhantanlinSystem` 只返回领域放置结果。
- Broadcast 的 WorldTip 通过 presentation port 输出；Systems 不直接触达 UI 提示控件。
- `tools/run_architecture_checks.*` 作为正式发布门禁，检查反射越界、Core/UI 反向依赖、Systems UI 依赖、UI 全世界扫描、未完成标记以及已知层级债务。
- 新功能若再次让 `Core/Systems` 直接操作 UI、在 UI 扫 `World.world.*`、或在 Interop 之外安装 Harmony/反射，视为 1.0 架构回归。

### 本轮 1.0 RC 进一步削峰

- 家族族议从同帧全家族循环改为 `BeginYear/TickPending` 游标事务，年度主车道在事务完成前保持同一阶段；业务机会不丢失，只把 CPU 峰值摊到后续渲染帧。
- 龙属洞天旧档共享建筑恢复改用金丹索引中的持久锚点，不再遍历 `World.world.buildings`。
- 旃檀林矿物/路径/角色兼容清理改为固定领域包围盒、局部 chunk 与释修索引；放置/读档修复的代价不再随全世界实体数量增长。
- 百年世谱同一卷只获取一次最多 8000 条历史证据，重要事件与代表人物复用该快照，去掉第二轮逐条 Clone 与对应 GC 峰值。
- `XjScheduler.RuntimeLanes.cs` 单独承载后台/死亡车道组合，中央 Scheduler 保留角色年度事务与预算，不再继续吸收新模块注册。
- `Interop/WorldBox/Legacy` 的生产 C# 已清零：领域系统、UI 与持久化文件已迁回真实归属目录，仍在运行的 Harmony 入口统一位于 `Interop/WorldBox/Patches`。Patch 仅负责原生 Hook/Adapter/安全边界；运行期属性等业务投影已迁回 Systems，后续不得把业务规则重新塞回 Patch 或重建 Legacy 目录。


## 0.9.9.9 工程借鉴 R2：原生边界与无效控制面退场

- Interop 外 WorldBox 私有反射债务已收束为 0；运行配置、Canon 元数据、ActorDataKeys 枚举等 3 路本地元数据反射单独审查，不与原生债务混为一谈。
- 跨帧 Actor/City 引用默认保存稳定 ID。bootstrap、宗门五年队列、Army 恢复队列和复用 UI 已执行该规则；`XjActorRegistry/XjWorldLookupIndex` 是唯一解析器例外，无稳定 ID 的 Army 与第三方替换源实例仅允许有界事务引用。
- 同一个 WorldBox 私有结构只能存在一个兼容入口：地图坐标、ActorAsset、渲染/生存字段、Building、Projectile、阵法占领、登名石、Archive custom-data 等访问统一位于 `Interop/WorldBox`。
- 无消费者/无效果的基础设施必须退场：空壳 Native Fault Recovery、伪 WorldLog WeakReference Pruner、未使用 FeatureGate、零订阅 DomainEventHub 已删除；不得因“未来可能使用”预留后台车道、队列或遥测。
- 世界清档 ownership 保持单一路径；同一运行缓存不得由 EventBus、CacheRegistry、Scheduler 重复 fan-out 清理。
- `tools/xj_architecture_guard.py` 对已退役控制面、长期原生引用、直接 native ID resolver、Interop 外反射设置窄规则，后续若确有新需求必须显式修改架构决策，不能静默绕回旧结构。
- 本轮不改变 Scheduler 年度语义、玩法数值、触发率、事件内容或存档 schema；正式编译、存读档、长局与性能验收统一留在工程修改结束后。

## 0.9.9.9 工程借鉴 R6：Load / Save / Bootstrap 生命周期

- `MapBox.finishingUpLoading` 只是 NativeLoaded ingress，不代表玄鉴 feature state 已 ready。任何模块级 `WorldLifecycle.Loaded` 都必须等 `XjWorldArchiveSystem.HasLoadedArchive` 成立后由 `XjWorldBootstrapLane` 唯一发布。
- 加载顺序固定为 `NativeLoaded -> ArchiveReady -> FeatureLoaded -> BootstrapComplete -> NormalRuntime`。归档未 ready 时不得让默认 runtime state 成为持久权威；feature-loaded 后登记的后台工作不得越过 bootstrap actor/index rehydration。
- Bootstrap 冷路径只在 ArchiveReady 后一次性读取 live units 并压成 ActorId 快照；跨帧恢复不得保存 Actor/City 原生实例。无稳定 ID 的 Army 只允许作为 BootstrapComplete 后的短事务引用。
- ArchiveReady 到 BootstrapComplete 期间，玄鉴 RuntimeCadence 只能推进 bootstrap；血脉、地形、Army inspection、后台归档、Growth/Fast/Background 等普通维护必须等待。WorldBox 原生模拟不由玄鉴冻结。
- `SaveManager.currentWorldToSavedMap` 是同步 correctness boundary，但**不是 bootstrap 执行器**：若 bootstrap 尚未完成，只允许保留已加载的 archive authority、跳过会把半恢复态写成新权威的玄鉴归档提交；不得为了保存同步跑完 bootstrap、建城建国或执行玩法修复。原生世界保存由 WorldBox 自己完成。
- Load callback 不执行全量诊断扫描。三书 after-load audit 只登记 pending，由正常年度 Diagnostics 阶段消费；最终长局审计仍属于发布验收，不回到生产加载路径。
- 世界清理必须同时释放 runtime pause / resolver / bootstrap / archive 等世界级状态；UI pause 状态不得跨 world switch 泄漏。
- R6 不重写 Scheduler Pending/Catch-up/Persistence，不新增第二套 load state machine，不复制鬼谷的主模拟冻结或广泛 Finalizer 吞异常。

## 0.9.9.8 Native Authority 收权：禁止二次构建原生生命周期

本节覆盖旧版中“保存时完成 bootstrap”“治理家族同步原生城主”等兼容性约定。当前原则是：**WorldBox 已有生命周期的对象只允许 WorldBox 完成一次事务；玄鉴只表达自己的规则、资格、派生状态和只读投影。**

- **Actor Trait**：`Actor.addTrait/removeTrait` 的玄鉴 Harmony 所有权统一为 `XjNativeTraitMutationPatches`。资质、境界、身份、阴司、剑道、调试命令等不得再分别 Patch 同一原生入口。权威玄鉴状态向可见 Trait 单向投影；只有显式手动 Trait 编辑上下文才允许反向解释一次。
- **Save**：`SaveManager.currentWorldToSavedMap` 与 `Actor.prepareForSave/saveKingdomCiv` 是序列化边界，不是玩法修复入口。保存时不得建城、建国、同步跑完 bootstrap、生成镜影、修角色职业/寻路/治理关系。允许的例外只限“把已经发生的玄鉴语义债务持久化”和当前军队旧档断裂的窄引用清理。
- **City / Kingdom**：城市转国只调用一次 WorldBox 原生转国事务并验证结果；不得随后逐居民 `setKingdom`、`forceBuildingsToKingdom`、`switchedKingdom` 或再执行一次回滚式转国。玄鉴宗门治理家族是政治事实，不再强制覆盖 WorldBox 原生 city leader/mayor。
- **Path / Movement**：正常移动与路径由 WorldBox `goTo/RegionPathFinder` 权威维护。玄鉴不得直接写 `current_path` 或 `setTileTarget` 伪造路径。只在太虚穿梭、洞天/旃檀林跨界、闭关入驻等明确“实体转移”语义中使用 `spawnOn`；坏路线只能作废原生路径并退避，不创建第二路径器。
- **Army**：军队创建、征兵、队长、编制、City/ArmyManager 与保存恢复全部由 WorldBox 唯一权威。玄鉴不得再筛选原生参军资格、拆军、认定“同城重复 Army”、反射修引用或在保存/读档时改写 Army。宗门战争只发起玄鉴自己的战斗意图，不接管原生 Army 生命周期。
- **Synthetic Actor**：登名石、转世、龙属等显式生成只负责生成角色。文明角色允许保持“干净的无城无国”状态，由 WorldBox 原生 AI 后续决定加入/建立聚落；玄鉴只允许把“角色已经处于某座原生城市，但 Kingdom 指针漏挂”修回该城市现有 Kingdom。生成、运行恢复、AI、保存均不得因此制造 City/Kingdom。
- **旃檀林建城门禁**：只 Patch 当前支持版本已验证的精确原生建城入口；目标缺失时 fail-soft。禁止重新扫描 `Assembly-CSharp` 并按 `create/new/build/settle` 等字符串猜测业务方法。
- **Native metadata**：国家名等原生展示元数据优先在创建事件精确处理；旧档只允许一次有界初始对账。禁止因为“可能被改回”而永久年度遍历全部 Kingdom/Family/Religion 重新写名。
- **异常 Finalizer**：只允许吞掉“已确认是原生异常 + 已能让坏对象退役/进入有界恢复”的异常。包含 `XuanJianVNext` 栈帧的异常必须向上暴露，禁止把玄鉴自身错误伪装成原生恢复。

新增功能在设计阶段必须先回答：**这个对象的生命周期是不是 WorldBox 已经拥有？** 如果答案是“是”，玄鉴默认只能选择“前置准入、一次原生事务、结果监听、dirty/ID 队列、只读投影”中的一种或数种，不能建立镜像生命周期。


## R8 发布硬标准：Native Authority + Long-run Performance Gate

以下不是建议，而是 **1.0 之前所有新增/修改代码必须通过的硬门禁**。若确有例外，必须先修改本节并写明生命周期所有权、触发频率、上界和回收策略，禁止在业务代码中静默绕过。

1. **原生生命周期单写者**：WorldBox 已拥有 Trait、City/Kingdom、Army、Path、原生 AI、Building 等生命周期时，玄鉴只能做前置准入、一次原生事务、结果监听、dirty/ID 队列或只读投影；不得再建第二套对象状态机。
2. **Trait 唯一入口**：玄鉴对 `Actor.addTrait/removeTrait` 的 Harmony 只允许存在于 `XjNativeTraitMutationPatches`。业务模块不得直接再 Patch 同一入口。
3. **Save / UI / Tooltip / ReadModel 只读边界**：这些路径不得建城、建国、修军队、推进境界、补功法、运行 bootstrap 或改变原生 AI/寻路。保存只持久化已发生事实，UI 只读取快照。
4. **Path 原生权威**：禁止写 `current_path`、禁止 `setTileTarget` 伪造路径。玄鉴超凡位移只能在明确跨界/瞬移语义中调用原生位置事务；普通移动回到 `goTo/RegionPathFinder`。
5. **文明对象禁止补造**：业务运行、恢复与保存中禁止 `makeNewCivKingdom`、`buildCityAndStartCivilization`；干净的无城无国角色是合法中间状态。
6. **世界扫描只能是冷路径**：`getSimpleList()` / 全世界实体遍历不得进入 `Actor.update/updateAge/getHit`、年度单角色管线、UI 刷新和普通后台帧。现有全表读取仅允许 bootstrap/明确的一次性兼容迁移白名单。
7. **跨帧只存稳定 ID**：新的长期 `static List/Queue/Dictionary/HashSet<Actor|City|Kingdom>` 默认禁止。必须跨帧时存 ID；原生实例只允许短事务 scratch/interop 白名单，并必须有 Clear/Remove。
8. **所有队列必须有四件套**：去重、容量/高水位、每帧条目预算、墙钟预算。禁止 `while(timer >= interval)` 式无限追债；高倍速只保留语义债务，分帧消费。
9. **事件驱动优先于周期修复**：能在权威写入口失效/dirty 的，不得再每年、每5年“为了保险”全量 Reconcile。兼容修复必须是一次迁移，或只在状态签名/revision 变化后再执行。
10. **热路径禁止 JSON 重建**：Combat/Runtime/Harmony 热入口不得反序列化 JSON。年度角色链中的 JSON 集合检查必须优先改为写时维护的标量摘要、revision-keyed 只读缓存或状态签名门控。
11. **异常不是调度器**：Finalizer 只有在异常签名已知且坏对象能被真正退役时才能吞异常。禁止“任何 NRE -> null”；同一对象异常不得无退避地每年/每帧重试并刷日志。
12. **禁止主动 GC**：生产代码不得调用 `GC.Collect()` / `Resources.UnloadUnusedAssets()` 解决逻辑泄漏。先裁剪持久集合、释放快照、重建高水位容器，再由 CLR/Unity 自行 GC。
13. **修炼体系不变量统一**：仙修与释修都属于玄鉴托管修炼体系；正常修士不得保留/重新获得 `madness`。金性妖邪是明确例外，例外必须由唯一 Trait Router 锁定。
14. **求金兼容修复不得成为年度玩法**：五神通/五部五品映射的旧档修复只能在结构状态签名变化时执行一次；正常求金只读真实功法结构并消费既有机会时钟，禁止每年反复 JSON Reconcile。
15. **发布必须看趋势，不只看瞬时 FPS**：同一压测配置至少记录 P95/P99 frame、>50/>100/>200ms 慢帧、GC0/1/2、managed/process memory delta、年度债务 oldest-year、队列 high-water。长档验收关注这些指标是否随年份单调恶化。
16. **Memory prune 必须两层化**：x20/x40 只允许执行有条目预算和墙钟预算的 stable-ID 语义回收，不得 `TrimExcess`、重建 Dictionary/Queue 或释放整组 snapshot；真正的 storage rebuild 仍只允许 `StressTier == Normal && speed <= 2x`。Actor stale-ID 至少经过多次独立维护 pass 的原生单位索引缺失确认，确认后只调用统一 `ForgetUnavailableActor` 清当前运行态，绝不伪造死亡、历史、继承、奖励。
17. **stale-ID 不得靠全表扫兜底**：已知死亡/移除必须 O(1) 从 tracker/运行态索引退出；漏事件只由 bounded probe 自愈。City/Kingdom 正缓存若在一次 O(1) 读取中发现对象失效，可只删除该单条并请求后台 reconciliation；宗门/家族等历史 ID 不得因“当前无人存活”被当作 stale 实体删除。

`tools/xj_architecture_guard.py` 是上述硬标准的最低静态门禁。它不替代编译/实机压测，但任何静态失败都视为架构回归，禁止以“运行看起来没问题”为由跳过。


### P0 Native Authority Isolation（R8-19）
- UnitWindow / CityWindow / KingdomWindow 的布局、Tab、DragOrder、动画与原生既有 Stat 行归 WorldBox 唯一所有；禁止持续重排、强制布局、替换原生节点或按帧/按年刷新。
- R8-25 仅为 CityWindow / KingdomWindow 开放一项只读例外：允许在 `showStatsRows` Postfix 中通过 `StatsRowsContainer.getStatRow` 绑定玄鉴自有的“修炼者”统计行，并用 `setMetaForTooltip` 展开境界分布。统计必须读取 `XjCultivatorCache`，不得扫描全世界人口；不得触碰 `startShowingWindow`、WindowMetaTab、DragOrder、Layout/Tween、窗口关闭历史，也不得建立延迟/周期刷新。
- UnitWindow 仅保留经门禁列明的窄扩展：右侧玄鉴照录、固定工具按钮，以及 R8-24 StatsIcon 桥。R8-24 只允许从 `i_kills` 复制视觉模板、在结构缺失时静态插入核心三值，并在窗口重新启用后的下一帧仅重绑数值；不得改写原生 LayoutGroup/RectTransform 参数，也不得 Harmony 接管 `OnEnable/showStatsRows`。
- Avatar / Tooltip / Banner 原生 UI 不允许通过泛 NRE Finalizer 吞异常来维持“半成功刷新”。
- Army / captain / warrior / City.army / ArmyManager 由 WorldBox 唯一写入；玄鉴的旧 Army sanitizer 与高境排军均退役。
- 其余扩展信息优先使用玄鉴自有窗口、百科、排行榜或独立入口，不参与原生窗口/军队对象生命周期。
