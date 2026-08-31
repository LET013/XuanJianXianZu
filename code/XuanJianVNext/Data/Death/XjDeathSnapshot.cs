namespace XuanJianVNext.Data.Death;

internal readonly struct XjDeathSnapshot
{
	internal static XjDeathSnapshot Empty { get; } = new XjDeathSnapshot(
		found: false,
		actorId: 0L,
		name: string.Empty,
		raceKey: string.Empty,
		familyStableId: 0L,
		realmId: string.Empty,
		daoTu: string.Empty,
		gongFaName: string.Empty,
		gongFaGrade: 0,
		gongFaStage: 0,
		gongFaProgress: 0f,
		qiuJinFaName: string.Empty,
		qiuJinFaSourceGongFaName: string.Empty,
		qiuJinFaSourceGongFaGrade: 0,
		qiuJinFaBoundAuthority: string.Empty,
		jinXing: string.Empty,
		jinXingSource: string.Empty,
		guoWei: string.Empty,
		guoWeiZhongAi: string.Empty,
		jinDanYiXiang: 0,
		jinDanStageIndex: 0,
		isJieLinXian: false,
		isYuYiXian: false,
		faBao: string.Empty,
		faBaoId: string.Empty,
		faBaoDaoTu: string.Empty,
		faBaoClass: string.Empty,
		faBaoSource: string.Empty,
		renDan: string.Empty,
		quanBing: string.Empty,
		dongTian: string.Empty,
		qianKunDai: string.Empty,
		caiQiFaName: string.Empty,
		caiQiFaDaoTu: string.Empty,
		caiQiFaSourcePlace: string.Empty,
		year: 0,
		reasonCode: "Empty");

	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string Name;
	internal readonly string RaceKey;
	internal readonly long FamilyStableId;
	internal readonly string RealmId;
	internal readonly string DaoTu;
	internal readonly string GongFaName;
	internal readonly int GongFaGrade;
	internal readonly int GongFaStage;
	internal readonly float GongFaProgress;
	internal readonly string QiuJinFaName;
	internal readonly string QiuJinFaSourceGongFaName;
	internal readonly int QiuJinFaSourceGongFaGrade;
	internal readonly string QiuJinFaBoundAuthority;
	internal readonly string JinXing;
	internal readonly string JinXingSource;
	internal readonly string GuoWei;
	internal readonly string GuoWeiZhongAi;
	internal readonly int JinDanYiXiang;
	internal readonly int JinDanStageIndex;
	internal readonly bool IsJieLinXian;
	internal readonly bool IsYuYiXian;
	internal readonly string FaBao;
	internal readonly string FaBaoId;
	internal readonly string FaBaoDaoTu;
	internal readonly string FaBaoClass;
	internal readonly string FaBaoSource;
	internal readonly string RenDan;
	internal readonly string QuanBing;
	internal readonly string DongTian;
	internal readonly string QianKunDai;
	internal readonly string CaiQiFaName;
	internal readonly string CaiQiFaDaoTu;
	internal readonly string CaiQiFaSourcePlace;
	internal readonly int Year;
	internal readonly string ReasonCode;
	internal readonly long LastAttackerId;
	internal readonly string LastAttackerName;
	internal readonly string LastAttackerDaoTu;
	internal readonly int LastAttackerTier;

	internal XjDeathSnapshot(
		bool found,
		long actorId,
		string name,
		string raceKey,
		long familyStableId,
		string realmId,
		string daoTu,
		string gongFaName,
		int gongFaGrade,
		int gongFaStage,
		float gongFaProgress,
		string qiuJinFaName,
		string qiuJinFaSourceGongFaName,
		int qiuJinFaSourceGongFaGrade,
		string qiuJinFaBoundAuthority,
		string jinXing,
		string jinXingSource,
		string guoWei,
		string guoWeiZhongAi,
		int jinDanYiXiang,
		int jinDanStageIndex,
		bool isJieLinXian,
		bool isYuYiXian,
		string faBao,
		string faBaoId,
		string faBaoDaoTu,
		string faBaoClass,
		string faBaoSource,
		string renDan,
		string quanBing,
		string dongTian,
		string qianKunDai,
		string caiQiFaName,
		string caiQiFaDaoTu,
		string caiQiFaSourcePlace,
		int year,
		string reasonCode,
		long lastAttackerId = 0L,
		string lastAttackerName = "",
		string lastAttackerDaoTu = "",
		int lastAttackerTier = 0)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		Name = name ?? string.Empty;
		RaceKey = raceKey ?? string.Empty;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		RealmId = realmId ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		GongFaName = gongFaName ?? string.Empty;
		GongFaGrade = gongFaGrade < 0 ? 0 : gongFaGrade;
		// 兼容构造参数继续保留，旧档值在读取边界直接废弃。
		_ = gongFaStage;
		_ = gongFaProgress;
		GongFaStage = 0;
		GongFaProgress = 0f;
		QiuJinFaName = qiuJinFaName ?? string.Empty;
		QiuJinFaSourceGongFaName = qiuJinFaSourceGongFaName ?? string.Empty;
		QiuJinFaSourceGongFaGrade = qiuJinFaSourceGongFaGrade < 0 ? 0 : qiuJinFaSourceGongFaGrade;
		QiuJinFaBoundAuthority = qiuJinFaBoundAuthority ?? string.Empty;
		JinXing = jinXing ?? string.Empty;
		JinXingSource = jinXingSource ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		GuoWeiZhongAi = guoWeiZhongAi ?? string.Empty;
		JinDanYiXiang = jinDanYiXiang < 0 ? 0 : jinDanYiXiang;
		JinDanStageIndex = jinDanStageIndex < 0 ? 0 : jinDanStageIndex > 3 ? 3 : jinDanStageIndex;
		IsJieLinXian = isJieLinXian;
		IsYuYiXian = isYuYiXian;
		FaBao = faBao ?? string.Empty;
		FaBaoId = faBaoId ?? string.Empty;
		FaBaoDaoTu = faBaoDaoTu ?? string.Empty;
		FaBaoClass = faBaoClass ?? string.Empty;
		FaBaoSource = faBaoSource ?? string.Empty;
		RenDan = renDan ?? string.Empty;
		QuanBing = quanBing ?? string.Empty;
		DongTian = dongTian ?? string.Empty;
		QianKunDai = qianKunDai ?? string.Empty;
		CaiQiFaName = caiQiFaName ?? string.Empty;
		CaiQiFaDaoTu = caiQiFaDaoTu ?? string.Empty;
		CaiQiFaSourcePlace = caiQiFaSourcePlace ?? string.Empty;
		Year = year < 0 ? 0 : year;
		ReasonCode = reasonCode ?? string.Empty;
		LastAttackerId = lastAttackerId < 0L ? 0L : lastAttackerId;
		LastAttackerName = lastAttackerName ?? string.Empty;
		LastAttackerDaoTu = lastAttackerDaoTu ?? string.Empty;
		LastAttackerTier = lastAttackerTier < 0 ? 0 : lastAttackerTier;
	}
}
