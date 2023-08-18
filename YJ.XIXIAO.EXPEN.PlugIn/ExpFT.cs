using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Kingdee.BOS;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.CommonFilter;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.DynamicForm.PlugIn.ControlModel;
using Kingdee.BOS.Core.DynamicForm.PlugIn.WizardForm;
using Kingdee.BOS.Core.List;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Core.Metadata.EntityElement;
using Kingdee.BOS.Core.SqlBuilder;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Orm.Metadata.DataEntity;
using Kingdee.BOS.Resource;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.VerificationHelper;
using Kingdee.K3.FIN.Business.PlugIn;
using Kingdee.K3.FIN.Core;
using Kingdee.K3.FIN.FA.ServiceHelper;

namespace YJ.XIXIAO.EXPEN.PlugIn
{
    [Description("固定资产计提折旧-表单插件")]
    public class ExpFT: AbstractWizardFormPlugIn
    {
		private const string FDEPRFROM = "FDEPRFROM";

		private const string FDEPRRESULT = "FDEPRRESULT";

		private const string FSELECT = "FSELECT";

		private const string FOWNERORGIDFROM = "FOWNERORGIDFROM";

		private const string FOWNERORGNAMEFROM = "FOWNERORGNAMEFROM";

		private const string FACCTPOLICYIDFROM = "FACCTPOLICYIDFROM";

		private const string FACCTPOLICYNAMEFROM = "FACCTPOLICYNAMEFROM";

		private const string FYEARPERIODFROM = "FYEARPERIODFROM";

		private const string KEY_BTN_NEXT = "FNEXT";

		private const string KEY_BTN_FPREVIOUS = "FPREVIOUS";

		private const string KEY_BTN_FCANCEL = "FCancel";

		private const string KEY_BTN_DEPR = "FDEPR";

		private const string KEY_BTN_MYCANCEL = "FMYCANCEL";

		private const string KEY_BTN_FBRESULT = "FBRESULT";

		private const string KEY_BTN_FINISH = "FFINISH";

		private int currentRowIndex = -1;

		private int currentStep;

		private Dictionary<string, string> _ditPolicyOwnerPeriodInfo = new Dictionary<string, string>();

		private Dictionary<string, string> dicPolicyOwnerPeriodInfo = new Dictionary<string, string>();

		public override void PreOpenForm(PreOpenFormEventArgs e)
		{
			((AbstractDynamicFormPlugIn)this).PreOpenForm(e);
			//LicenseVerifier.CheckViewOnlyOperation(this.Context, ResManager.LoadKDString("计提折旧", "003268000005860", (SubSystemType)4, new object[0]));
		}

		public override void AfterBindData(EventArgs e)
		{
			((AbstractDynamicFormPlugIn)this).AfterBindData(e);
			LockTable();
			SetStatusProperty(0);
			((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.SetValue("FSAVELAST", (object)true);
			((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).GetControl<EntryGrid>("FDEPRRESULT").SetFireDoubleClickEvent(true);
		}

		private string GetOrgSeparate()
		{
			List<long> permissionOrgIdList = BaseFunction.GetPermissionOrgIdList(this.Context, (IDynamicFormView)(object)((AbstractWizardFormPlugIn)this).View, "37951259afb74104bfd242c849e3d7f0");
			return string.Join(",", permissionOrgIdList);
		}

		public override void OnInitialize(InitializeEventArgs e)
		{
			((AbstractDynamicFormPlugIn)this).OnInitialize(e);
			this.View.GetControl<Button>("FDEPR").SetCustomPropertyValue("ShowProgressBar", true);
		}

		public override void BeforeBindData(EventArgs e)
		{
			((AbstractDynamicFormPlugIn)this).BeforeBindData(e);
			BindDeprData();
			((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).UpdateView("FDEPRFROM");
		}

		private void BindDeprData()
		{
			//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d0: Expected O, but got Unknown
			string text = "";
			Entity entity = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).BusinessInfo.GetEntity("FDEPRFROM");
			DynamicObjectType dynamicObjectType = entity.DynamicObjectType;
			DynamicObjectCollection value = entity.DynamicProperty.GetValue<DynamicObjectCollection>(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.DataObject);
			((Collection<DynamicObject>)(object)value).Clear();
			string orgSeparate = GetOrgSeparate();
			DynamicObjectCollection ownerAndPolicy = AssetDeprServiceHelper.GetOwnerAndPolicy(this.Context, orgSeparate);
			if (ownerAndPolicy == null || ((IEnumerable<DynamicObject>)ownerAndPolicy).Count() == 0)
			{
				return;
			}
			new List<long>();
			int num = 0;
			foreach (DynamicObject item in (Collection<DynamicObject>)(object)ownerAndPolicy)
			{
				if (item["FCurrentYear"] != null)
				{
					text = ((item["FYearPeriod"] != null) ? item["FYearPeriod"].ToString() : "");
					DynamicObject val = (DynamicObject)dynamicObjectType.CreateInstance();
					val["FSELECT"] = 1;
					val["FACCTPOLICYIDFROM"] = item["FACCTPOLICYID"].ToString();
					val["FACCTPOLICYNAMEFROM"] = item["FACCTPOLICYNAME"].ToString();
					val["FYEARPERIODFROM"] = text;

				
					((Collection<DynamicObject>)(object)value).Add(val);
					((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.SetValue("FOWNERORGIDFROM", (object)item["FOWNERORGID"].ToString(), num);
					num++;
					string key = string.Format("{0}_{1}", item["FACCTPOLICYID"], item["FOWNERORGID"]);
					string value2 = string.Format("{0}_{1}", item["FCURRENTYEAR"], item["FCURRENTPERIOD"]);
					if (dicPolicyOwnerPeriodInfo.Keys.Count > 0)
					{
						val["FSELECT"] = dicPolicyOwnerPeriodInfo.ContainsKey(key);
					}
					else
					{
						val["FSELECT"] = 1;
					}
					_ditPolicyOwnerPeriodInfo[key] = value2;
				}
			}
		}

		public override void ButtonClick(ButtonClickEventArgs e)
		{
			switch (e.Key.ToUpperInvariant())
			{
				case "FDEPR":
					if (currentStep == 1)
					{
						SetStatusProperty(0);
						BindDeprData();
						currentStep = 0;
						break;
					}
					currentStep = 1;
					if (!AssetDeprMain())
					{
						e.Cancel = true;
					}
					break;
				case "FMYCANCEL":
					((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Close();
					break;
				case "FBRESULT":
					ShowDeprAdjust(currentRowIndex);
					break;
			}
			((AbstractDynamicFormPlugIn)this).ButtonClick(e);
		}

		public override void EntityRowClick(EntityRowClickEventArgs e)
		{
			currentRowIndex = e.Row;
		}

		public override void EntityRowDoubleClick(EntityRowClickEventArgs e)
		{
			if (e.Key == "FDEPRRESULT")
			{
				ShowDeprAdjust(e.Row);
			}
		}

		private void LockTable()
		{
			this.View.GetFieldEditor("FACCTPOLICYNAMEFROM", -1).Enabled = false;
			this.View.GetFieldEditor("FYEARPERIODFROM", -1).Enabled = false;
		}

		private bool AssetDeprMain()
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
			bool bIsSelAll = false;
			if (!CheckUnAuditWorkLoadBills())
			{
				return false;
			}
			list = GetSelectData(out bIsSelAll);
			if (list.Count == 0)
			{
				((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowMessage(ResManager.LoadKDString("请选择需要计提折旧的记录！", "003268000005842", (SubSystemType)4, new object[0]), (MessageBoxType)0);
				return false;
			}
			this.View.GetControl<Button>("FDEPR").Text = (ResManager.LoadKDString("正在计提折旧", "003268000005845", (SubSystemType)4, new object[0]));
			this.View.GetControl<Button>("FDEPR").Enabled = false;
			bool flag = (bool)((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FSAVELAST");
			dictionary.Add("ISSAVE", flag);
			dictionary.Add("ISSALALL", bIsSelAll);
			dictionary.Add("FALTERID", -1);
			dictionary.Add("FOLDALTERID", -1);
			Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
			Dictionary<string, List<string>> unCheckedChangeAndDispose = GetUnCheckedChangeAndDispose(list);
			List<Dictionary<string, object>> list2 = new List<Dictionary<string, object>>();
			if (unCheckedChangeAndDispose != null && unCheckedChangeAndDispose.Count > 0)
			{
				foreach (Dictionary<string, object> item in list)
				{
					string key = string.Format("{0}_{1}", item["FACCTPOLICYID"], item["FOWNERORGID"]);
					if (unCheckedChangeAndDispose.ContainsKey(key))
					{
						List<string> list3 = unCheckedChangeAndDispose[key];
						int num = 0;
						int num2 = 0;
						int num3 = 0;
						int num4 = 0;
						int num5 = 0;
						int num6 = 0;
						int num7 = 0;
						int num8 = 0;
						int num9 = 0;
						int num10 = 0;
						int num11 = 0;
						foreach (string item2 in list3)
						{
							switch (item2.Split('_')[0].ToUpperInvariant())
							{
								case "CHANGE":
									num = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "BATCHCHANGE":
									num2 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "DISPOSAL":
									num3 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "DISPOSALAPPLY":
									num4 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "CARD":
									num5 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "DELIVERY":
									num6 = Convert.ToInt32(item2.Split('_')[1]);
									if (num6 > 0)
									{
										dictionary2[key] = item2;
									}
									break;
								case "WORKLOAD":
									num7 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "STOCKRETURN":
									num9 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "PICKING":
									num8 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "TRANSFERING":
									num10 = Convert.ToInt32(item2.Split('_')[1]);
									break;
								case "PICKAPPLY":
									num11 = Convert.ToInt32(item2.Split('_')[1]);
									break;
							}
						}
						string text = string.Empty;
						if (num + num2 + num3 + num5 + num7 + num9 + num10 + num4 + num8 > 0)
						{
							if (num > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张变更单未审核，", "003268000022377", (SubSystemType)4, new object[0]), num);
							}
							if (num2 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张批量变更单未审核，", "003268000033477", (SubSystemType)4, new object[0]), num2);
							}
							if (num3 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张处置单未审核，", "003268000022378", (SubSystemType)4, new object[0]), num3);
							}
							if (num4 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张人人资产处置申请单未审核", "003268000022025", (SubSystemType)4, new object[0]), num4);
								text += "，";
							}
							if (num5 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张资产卡片未审核，", "003268000033476", (SubSystemType)4, new object[0]), num5);
							}
							if (num7 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张资产卡片未维护工作量", "003268000038698", (SubSystemType)4, new object[0]), num7);
								text += "，";
							}
							if (num8 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张资产领用单未审核", "003268000025973", (SubSystemType)4, new object[0]), num8);
								text += "，";
							}
							if (num9 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张资产退库单未审核", "003268000021552", (SubSystemType)4, new object[0]), num9);
								text += "，";
							}
							if (num10 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张资产转移单未审核", "003268000021560", (SubSystemType)4, new object[0]), num10);
								text += "，";
							}
							if (num11 > 0)
							{
								text += string.Format(ResManager.LoadKDString("有{0}张人人资产领用未审核", "003268000021814", (SubSystemType)4, new object[0]), num11);
								text += "，";
							}
							text = (dictionary2[key] = text + ResManager.LoadKDString("请先处理后再计提！", "003268000022379", (SubSystemType)4, new object[0]));
						}
						else
						{
							list2.Add(item);
						}
					}
					else
					{
						list2.Add(item);
					}
				}
			}
			else
			{
				list2 = list;
			}
			DynamicObjectCollection val = AssetDeprServiceHelper.AssetDepr(this.Context, list2, dictionary);
			if (currentStep == 1)
			{
				SetStatusProperty(1);
			}
			else
			{
				SetStatusProperty(0);
			}
			if (((Collection<DynamicObject>)(object)val).Count > 0 || dictionary2.Count > 0)
			{
				BandResultData(val, list, dictionary2);
			}
			else
			{
				BandEmptyData();
			}
			this.View.GetControl<Button>("FDEPR").Text  = ResManager.LoadKDString("上一步", "003268000005848", (SubSystemType)4, new object[0]);
			this.View.GetControl<Button>("FDEPR").Enabled = true;
			FACommonServiceHelper.UpdateDeprAjustExtendInfo(this.Context, dicPolicyOwnerPeriodInfo);
			return true;
		}

		private Dictionary<string, List<string>> GetUnCheckedChangeAndDispose(List<Dictionary<string, object>> lstOwnerPeriod)
		{
			List<int> list = new List<int>();
			List<string> list2 = new List<string>();
			foreach (Dictionary<string, object> item in lstOwnerPeriod)
			{
				int num = Convert.ToInt32(item["FACCTPOLICYID"]);
				list2.Add(string.Format("{0}_{1}_{2}_{3}", num, item["FOWNERORGID"], item["FYEAR"], item["FPERIOD"]));
				if (!list.Contains(num))
				{
					list.Add(num);
				}
			}
			return AssetDeprServiceHelper.GetUnCheckedChangeAndDisposeCount(this.Context, list, list2);
		}

		private void BandEmptyData()
		{
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Expected O, but got Unknown
			Entity entity = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).BusinessInfo.GetEntity("FDEPRRESULT");
			DynamicObjectType dynamicObjectType = entity.DynamicObjectType;
			DynamicObjectCollection value = entity.DynamicProperty.GetValue<DynamicObjectCollection>(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.DataObject);
			((Collection<DynamicObject>)(object)value).Clear();
			List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
			bool bIsSelAll = false;
			list = GetSelectData(out bIsSelAll);
			foreach (Dictionary<string, object> item in list)
			{
				DynamicObject val = (DynamicObject)dynamicObjectType.CreateInstance();
				val["FOwnerOrgName"] = item["FOWNERORGNAME"].ToString();
				val["FAcctPolicyName"] = item["FACCTPOLICYNAME"].ToString();
				val["FDESCRIPTION"] = ResManager.LoadKDString("没有任何卡片需要计提折旧", "003268000005851", (SubSystemType)4, new object[0]);
				val["FRESULT"] = ResManager.LoadKDString("异常", "003268000005854", (SubSystemType)4, new object[0]);
				val["FRESULTFLAG"] = 5;
				val["FOwnerOrgID"] = -1;
				val["FAcctpolicyid"] = -1;
				this.View.GetControl("FTotalTime").Text = "";
				((Collection<DynamicObject>)(object)value).Add(val);
			}
			((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).UpdateView(((AbstractElement)entity).Key);
		}

		private void BandResultData(DynamicObjectCollection dcResult, List<Dictionary<string, object>> lstOwnerPolicy, Dictionary<string, string> ditUnCheckedChangeAndDispose)
		{
			//IL_0136: Unknown result type (might be due to invalid IL or missing references)
			//IL_013d: Expected O, but got Unknown
			//IL_0249: Unknown result type (might be due to invalid IL or missing references)
			//IL_0250: Expected O, but got Unknown
			string text = "";
			Entity entity = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).BusinessInfo.GetEntity("FDEPRRESULT");
			DynamicObjectType dynamicObjectType = entity.DynamicObjectType;
			DynamicObjectCollection value = entity.DynamicProperty.GetValue<DynamicObjectCollection>(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.DataObject);
			((Collection<DynamicObject>)(object)value).Clear();
			Dictionary<string, List<DynamicObject>> dictionary = new Dictionary<string, List<DynamicObject>>();
			foreach (DynamicObject item in (Collection<DynamicObject>)(object)dcResult)
			{
				string key = string.Format("{0}_{1}", item["FACCTPOLICYID"], item["FOWNERORGID"]);
				if (dictionary.ContainsKey(key))
				{
					dictionary[key].Add(item);
					continue;
				}
				dictionary.Add(key, new List<DynamicObject> { item });
			}
			foreach (Dictionary<string, object> item2 in lstOwnerPolicy)
			{
				string key2 = string.Format("{0}_{1}", item2["FACCTPOLICYID"], item2["FOWNERORGID"]);
				if (ditUnCheckedChangeAndDispose.ContainsKey(key2) && !ditUnCheckedChangeAndDispose[key2].StartsWith("DELIVERY"))
				{
					DynamicObject val = (DynamicObject)dynamicObjectType.CreateInstance();
					val["FOwnerOrgName"] = item2["FOWNERORGNAME"].ToString();
					val["FAcctPolicyName"] = item2["FACCTPOLICYNAME"].ToString();
					val["FDESCRIPTION"] = ditUnCheckedChangeAndDispose[key2];
					val["FRESULT"] = ResManager.LoadKDString("异常", "003268000005854", (SubSystemType)4, new object[0]);
					val["FRESULTFLAG"] = 5;
					val["FOwnerOrgID"] = item2["FOWNERORGID"].ToString();
					val["FAcctpolicyid"] = item2["FACCTPOLICYID"].ToString();
					val["FYear"] = item2["FYEAR"].ToString();
					val["FPeriod"] = item2["FPERIOD"].ToString();
					((Collection<DynamicObject>)(object)value).Add(val);
					continue;
				}
				List<DynamicObject> list = dictionary[key2];
				foreach (DynamicObject item3 in list)
				{
					DynamicObject val2 = (DynamicObject)dynamicObjectType.CreateInstance();
					val2["FOwnerOrgName"] = item3["FOwnerOrgName"].ToString();
					val2["FAcctPolicyName"] = item3["FAcctPolicyName"].ToString();
					string text2 = BillExtension.GetValue<string>(item3, "FDESCRIPTION", "");
					if (ditUnCheckedChangeAndDispose.ContainsKey(key2) && ditUnCheckedChangeAndDispose[key2].StartsWith("DELIVERY"))
					{
						text2 += string.Format(ResManager.LoadKDString("（提醒：存在{0}张已审核的调出单未下推处置单）", "003268000038829", (SubSystemType)4, new object[0]), ditUnCheckedChangeAndDispose[key2].Replace("DELIVERY_", ""));
					}
					val2["FDESCRIPTION"] = text2;
					val2["FRESULT"] = item3["FRESULT"].ToString();
					val2["FRESULTFLAG"] = item3["FRESULTFLAG"].ToString();
					val2["FOwnerOrgID"] = item3["FOwnerOrgID"].ToString();
					val2["FAcctpolicyid"] = item3["FAcctpolicyid"].ToString();
					val2["FOwnerOrgName"] = item3["FOwnerOrgName"].ToString();
					val2["FOwnerOrgName"] = item3["FOwnerOrgName"].ToString();
					val2["FYear"] = item3["FYear"].ToString() == "-1" ? "0" : item3["FYear"].ToString();
					val2["FPeriod"] = item3["FPeriod"].ToString() == "-1" ? "0" : item3["FPeriod"].ToString();
					if (text.Trim().Length == 0)
					{
						text = string.Format(ResManager.LoadKDString("共耗时 {0} 秒", "003268000005857", (SubSystemType)4, new object[0]), BillExtension.GetValue<string>(item3, "FTotalTime", ""));
						this.View.GetControl("FTotalTime").Text = text;
					}
					((Collection<DynamicObject>)(object)value).Add(val2);
				}
			}
			((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).UpdateView(((AbstractElement)entity).Key);
		}

		private List<Dictionary<string, object>> GetSelectData(out bool bIsSelAll)
		{
			//IL_0076: Unknown result type (might be due to invalid IL or missing references)
			//IL_007d: Expected O, but got Unknown
			dicPolicyOwnerPeriodInfo.Clear();
			int entryRowCount = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetEntryRowCount("FDEPRFROM");
			int num = 0;
			new List<string>();
			List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
			bIsSelAll = false;
			for (int i = 0; i < entryRowCount; i++)
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>();
				if (!Convert.ToBoolean(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FSELECT", i)))
				{
					continue;
				}
				DynamicObject val = (DynamicObject)((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FOWNERORGIDFROM", i);
				if (val != null)
				{
					long num2 = Convert.ToInt64(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FACCTPOLICYIDFROM", i));
					long num3 = Convert.ToInt64(val["Id"]);
					dictionary["FOWNERORGID"] = num3;
					dictionary["FOWNERORGNAME"] = Convert.ToString(val["Name"]);
					dictionary["FACCTPOLICYID"] = num2;
					dictionary["FACCTPOLICYNAME"] = Convert.ToString(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FACCTPOLICYNAMEFROM", i));
					dictionary["FYEAR"] = "";
					dictionary["FPERIOD"] = "";
					string key = $"{num2}_{num3}";
					string text = _ditPolicyOwnerPeriodInfo[key];
					dicPolicyOwnerPeriodInfo[key] = text;
					if (!string.IsNullOrWhiteSpace(text))
					{
						dictionary["FYEAR"] = text.Split('_')[0];
						dictionary["FPERIOD"] = text.Split('_')[1];
					}
					list.Add(dictionary);
					num++;
				}
			}
			if (entryRowCount == num)
			{
				bIsSelAll = true;
			}
			return list;
		}

		private bool CheckUnAuditWorkLoadBills()
		{
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Expected O, but got Unknown
			int entryRowCount = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetEntryRowCount("FDEPRFROM");
			for (int i = 0; i < entryRowCount; i++)
			{
				if (!Convert.ToBoolean(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FSELECT", i)))
				{
					continue;
				}
				DynamicObject val = (DynamicObject)((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FOWNERORGIDFROM", i);
				if (val != null)
				{
					long acctPolicyId = Convert.ToInt64(((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetValue("FACCTPOLICYIDFROM", i));
					long ownerOrgId = Convert.ToInt64(val["Id"]);
					DynamicObjectCollection existUnAuditBill = GetExistUnAuditBill(ownerOrgId, acctPolicyId);
					if (((Collection<DynamicObject>)(object)existUnAuditBill).Count > 0)
					{
						((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowErrMessage(string.Format(ResManager.LoadKDString("单据编号为[{0}]的[工作量管理]单据未审核，请先审核！", "003268000011122", (SubSystemType)4, new object[0]), ((Collection<DynamicObject>)(object)existUnAuditBill)[0]["FBillNo"].ToString()), "", (MessageBoxType)0);
						return false;
					}
				}
			}
			return true;
		}

		private DynamicObjectCollection GetExistUnAuditBill(long ownerOrgId, long acctPolicyId)
		{
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Expected O, but got Unknown
			DynamicObject currentYearPeriod = FACommonServiceHelper.GetCurrentYearPeriod(this.Context, acctPolicyId, ownerOrgId);
			if (currentYearPeriod == null)
			{
				return null;
			}
			int num = Convert.ToInt32(currentYearPeriod["FCurrentYear"]);
			int num2 = Convert.ToInt32(currentYearPeriod["FCurrentPeriod"]);
			QueryBuilderParemeter val = new QueryBuilderParemeter();
			val.FormId = "FA_WorkLoadManager";
			val.SelectItems = SelectorItemInfo.CreateItems("FID,FBillNo");
			val.FilterClauseWihtKey = $" FDocumentStatus !='C' and FDocumentStatus !='Z' and\r\n                                                       FOwnerOrgID={ownerOrgId} and FAcctPolicyId={acctPolicyId} and FYear={num} and FPeriod={num2}";
			QueryBuilderParemeter val2 = val;
			return QueryServiceHelper.GetDynamicObjectCollection(this.Context, val2, (List<SqlParam>)null);
		}

		private void SetStatusProperty(int iFlag)
		{
			this.View.GetControl<Button>("FPREVIOUS").Visible = false;
			this.View.GetControl<Button>("FNEXT").Visible = false;
			this.View.GetControl<Button>("FCancel").Visible = false;
			this.View.GetControl<Button>("FFINISH").Visible = false;
			if (iFlag == 0)
			{
				((AbstractWizardFormPlugIn)this).View.JumpToWizardStep("FWizard0", true);
				this.View.GetControl<Button>("FDEPR").Text = ResManager.LoadKDString("计提折旧", "003268000005860", (SubSystemType)4, new object[0]);
				this.View.GetControl<Button>("FMYCANCEL").Text = ResManager.LoadKDString("取消", "003268000005863", (SubSystemType)4, new object[0]);
			}
			else
			{
				((AbstractWizardFormPlugIn)this).View.JumpToWizardStep("FWizard1", true);
				this.View.GetControl<Button>("FDEPR").Text = ResManager.LoadKDString("上一步", "003268000005848", (SubSystemType)4, new object[0]);
				this.View.GetControl<Button>("FMYCANCEL").Text = ResManager.LoadKDString("完成", "003268000005866", (SubSystemType)4, new object[0]);
			}
		}

		private void ShowDeprAdjust(int lCurrRowIndex)
		{
			//IL_00da: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Expected O, but got Unknown
			//IL_01df: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e6: Expected O, but got Unknown
			string text = "";
			if (lCurrRowIndex == -1)
			{
				((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowMessage(ResManager.LoadKDString("请选择需要查看的记录！", "003268000005869", (SubSystemType)4, new object[0]), (MessageBoxType)0);
				return;
			}
			Entity entity = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).BusinessInfo.GetEntity("FDEPRRESULT");
			DynamicObjectCollection entityDataObject = ((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).Model.GetEntityDataObject(entity);
			if (entityDataObject == null || ((Collection<DynamicObject>)(object)entityDataObject).Count == 0)
			{
				((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowMessage(ResManager.LoadKDString("请选择需要查看的记录！", "003268000005869", (SubSystemType)4, new object[0]), (MessageBoxType)0);
				return;
			}
			DynamicObject val = ((Collection<DynamicObject>)(object)entityDataObject)[lCurrRowIndex];
			if (val == null)
			{
				((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowMessage(ResManager.LoadKDString("无法获取调整单数据！", "003268000005872", (SubSystemType)4, new object[0]), (MessageBoxType)0);
				return;
			}
			long num = Convert.ToInt64(val["FRESULTFLAG"]);
			DynamicObjectCollection deprAdjustByParam = DeprAdjustServiceHelper.GetDeprAdjustByParam(this.Context, val);
			switch (num)
			{
				case 3L:
					{
						ListShowParameter val4 = new ListShowParameter();
						val4.FormId = "FA_DEPREXCEPTION";
						val4.ParentPageId = this.View.PageId;
						val4.MultiSelect = true;
						val4.IsShowApproved = true;
						val4.OpenStyle.CacheId = val4.PageId;
						val4.IsLookUp = true;
						val4.Height = 600;
						val4.Width = 800;

						text = string.Format("FOwnerOrgID = {0} and FAcctpolicyid = {1} ", Convert.ToInt64(val["FOwnerOrgID"]), Convert.ToInt64(val["FAcctpolicyid"]));
						text += string.Format(" and FYear = {0} and FPeriod = {1} ", Convert.ToInt64(val["FYear"]), Convert.ToInt64(val["Fperiod"]));
						IRegularFilterParameter listFilterParameter = val4.ListFilterParameter;
						listFilterParameter.Filter = (listFilterParameter.Filter + text);
						((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowForm((DynamicFormShowParameter)(object)val4);
						break;
					}
				case 5L:
					break;
				default:
					{
						BillShowParameter val2 = new BillShowParameter();
						((DynamicFormShowParameter)val2).ParentPageId = (((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).PageId);
						if (((Collection<DynamicObject>)(object)deprAdjustByParam).Count != 1)
						{
							((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowMessage(ResManager.LoadKDString("无法获取调整单数据！", "003268000005872", (SubSystemType)4, new object[0]), (MessageBoxType)0);
							break;
						}
					((DynamicFormShowParameter)val2).FormId = "FA_ADJUST";
						val2.Status = ((OperationStatus)2);
						DynamicObject val3 = ((Collection<DynamicObject>)(object)deprAdjustByParam)[0];
						val2.PKey = (val3["FID"].ToString());
						((DynamicFormShowParameter)val2).OpenStyle.ShowType = ((ShowType)6);
						((IDynamicFormView)((AbstractWizardFormPlugIn)this).View).ShowForm((DynamicFormShowParameter)(object)val2);
						break;
					}
			}
		}
	}
}
