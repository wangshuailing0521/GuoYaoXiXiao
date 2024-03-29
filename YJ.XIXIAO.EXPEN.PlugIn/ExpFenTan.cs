﻿using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.DynamicForm.PlugIn.WizardForm;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Core.Metadata.FieldElement;
using Kingdee.BOS.Core.Metadata.FormElement;
using Kingdee.BOS.Log;
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;

namespace YJ.XIXIAO.EXPEN.PlugIn
{
    [Description("费用分摊")]
    [HotUpdate]
    public class ExpFenTan: AbstractWizardFormPlugIn
    {
        List<FTOrg> fTOrgList = new List<FTOrg>();
        List<FTOrg> difOrgList = new List<FTOrg>();
        List<Cost> deptCostList = new List<Cost>();
        List<Cost> costList = new List<Cost>();
        List<CustNum> custNumList = new List<CustNum>();
        List<DetailCost> detailCostList = new List<DetailCost>();
        List<ShareResult> shareResultList = new List<ShareResult>();

        public override void BeforeBindData(EventArgs e)
        {
            base.BeforeBindData(e);
        }

        public override void ButtonClick(ButtonClickEventArgs e)
        {
            base.ButtonClick(e);

            if (e.Key.ToUpperInvariant().Contains("FNEXT"))
            {
                shareResultList = new List<ShareResult>();

                CostShare();

                ShowResult();
            }

            if (e.Key.ToUpperInvariant().Contains("FDIFSHARE"))
            {
                shareResultList = new List<ShareResult>();

                DifShare();

                this.View.JumpToWizardStep("FWizard1",true);

                ShowResult();
            }
         
        }

        /// <summary>
        /// 费用分摊
        /// </summary>
        /// <exception cref="KDException"></exception>
        private void CostShare()
        {
            GetSelectData();

            if (fTOrgList.Count <= 0)
            {
                throw new KDException("", "请选择需要费用分摊的记录！");
            }

            foreach (var fTOrg in fTOrgList)
            {
                try
                {
                    ExpFT(fTOrg);
                }
                catch (Exception ex)
                {
                    Logger.Error("", ex.Message, ex);
                    shareResultList.Add(new ShareResult { Org = fTOrg, Status = "失败", Message = ex.Message });
                }
               
            }
        }

        /// <summary>
        /// 差额分摊
        /// </summary>
        private void DifShare()
        {
            GetSelectData();

            if (fTOrgList.Count <= 0)
            {
                throw new KDException("", "请选择需要分摊差额的记录！");
            }

            foreach (var fTOrg in fTOrgList)
            {
                try
                {
                    DifFT(fTOrg);
                }
                catch (Exception ex)
                {
                    Logger.Error("", ex.Message, ex);
                    shareResultList.Add(new ShareResult { Org = fTOrg, Status = "失败", Message = ex.Message });
                }

            }
        }

        #region 费用分摊
        private void ExpFT(FTOrg fTOrg)
        {
            //获取总成本
            GetSumCost(fTOrg);
            //获取数量
            GetNum(fTOrg);
            //总成本分摊到客户产品
            FTDetailCost(fTOrg);
            //创建成本分摊单
            CreateCostShare(fTOrg);
        }
        #endregion

        #region 差额分摊
        private void DifFT(FTOrg fTOrg)
        {
            string billType = "CBFT2";

            //获取需要计提的差额期间
            GetDifOrg(fTOrg);

            foreach (var difOrg in difOrgList)
            {
                //获取总差额
                deptCostList.Clear();
                costList.Clear();
                GetDifShareData(difOrg);

                //获取数量
                custNumList.Clear();
                GetNum(difOrg,true);

                //总成本分摊到客户产品
                FTDetailCost(difOrg);
                //创建成本分摊单
                CreateCostShare(difOrg, billType);
            }  
        }
        void GetDifOrg(FTOrg fTOrg)
        {
            deptCostList.Clear();
            costList.Clear();
            difOrgList.Clear();
            DynamicObjectCollection data = null;
            string sql = $@"
                SELECT  DISTINCT YEAR(A.FJTDate) FYEAR,MONTH(A.FJTDate) FPeriod
                  FROM  t_ER_ExpenseReimb A
                        INNER JOIN t_ER_ExpenseReimbEntry B
                        ON A.FID = B.FID
                        INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
	                    ON B.FExpenseDeptEntryID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                        ON C.FDEPTID = E.FRELATION
                 WHERE  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FDocumentStatus = 'C'
                   AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门 
                   AND  A.FOrgID = {fTOrg.OrgId}
                   AND  YEAR(A.FJTDate) <> 0
                ";

            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                FTOrg difOrg = new FTOrg();

                difOrg.OrgId = fTOrg.OrgId;
                difOrg.AcctPolicy = fTOrg.AcctPolicy;
                difOrg.Year = fTOrg.Year;
                difOrg.Period = fTOrg.Period;

                difOrg.BeginTime = fTOrg.BeginTime;
                difOrg.EndTime = fTOrg.EndTime;

                difOrg.JTYear = item["FYEAR"].ToString();
                difOrg.JTPeriod = item["FPeriod"].ToString();
                difOrg.JTBeginTime = $@"{difOrg.JTYear}-{difOrg.JTPeriod}-01";
                difOrg.JTEndTime = Convert.ToDateTime(difOrg.JTBeginTime).AddMonths(1).ToString("yyyy-MM-dd");
                difOrgList.Add(difOrg);
            }

            
            if (difOrgList.Count <= 0)
            {
                throw new Exception("未获取到对应期间的总成本!");
            }
        }

       
        #endregion

        private void GetSumCost(FTOrg fTOrg)
        {
            //1.化料成本，辅料成本的费用项目：
            //如果有财务应付，则获取财务应付的金额
            //如果没有财务应付，则获取暂估应付的金额
            //2.费用项目为折旧费：
            //获取折旧调整单上的金额
            //3.非化料成本，辅料成本，折旧费的费用项目：
            //如果费用计提单没有下推费用报销单，则获取费用计提单的金额
            //如果费用计提单已经下推费用报销单，则获取费用报销单的金额
            //4.获取金额的时候，需要根据车间进行分组获取金额，车间分为专用车间和非专用车间。
            //例：专用车间的成本只分摊专用车间的客户。非专用车间的成本只分摊非专用车间的客户

            deptCostList.Clear();
            costList.Clear();

            string sql = "";
            DynamicObjectCollection data = null;

            #region 1.获取其他出库的金额
            sql = $@"
                SELECT  A.FBillNo
                       ,A.FStockOrgId FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,AT.FNUMBER FBillType
                       ,BMG.FNUMBER FMaterialGroup
                       ,SUM(B.FAmount)FAmount
                       ,C.FDEPTID
                       ,D.FNUMBER FDeptType
                  FROM  T_STK_MISDELIVERY A WITH(NOLOCK)
		                INNER JOIN T_STK_MISDELIVERYENTRY B WITH(NOLOCK)
		                ON A.FID = B.FID
		                INNER JOIN T_BAS_BILLTYPE AT WITH(NOLOCK)
						ON A.FBILLTYPEID = AT.FBILLTYPEID
		                INNER JOIN T_BD_MATERIAL BM WITH(NOLOCK)
						ON B.FMATERIALID = BM.FMATERIALID
						INNER JOIN T_BD_MATERIALGROUP BMG WITH(NOLOCK)
						ON BM.FMATERIALGROUP = BMG.FID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON A.FDeptId = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门 
                   AND  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FStockOrgId = {fTOrg.OrgId}
                 GROUP  BY A.FStockOrgId,C.F_ora_Assistant,C.FDEPTID,AT.FNUMBER,BMG.FNUMBER,D.FNUMBER,A.FBillNo
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            sql = $@"
            SELECT FExpID,FNumber
              FROM T_BD_Expense
             WHERE FNumber IN ('ZZ1001','ZZ1002','ZZ1003','ZZ2301','ZZ0901')";
            DynamicObjectCollection expDatas = DBUtils.ExecuteDynamicObject(this.Context, sql);

            List<ExpInfo> expInfos = new List<ExpInfo>();
            foreach (var expData in expDatas)
            {
                ExpInfo expInfo = new ExpInfo();
                expInfo.ExpID = expData["FExpID"].ToString();
                expInfo.ExpNumber = expData["FNumber"].ToString();
                expInfos.Add(expInfo);
            }

            if (expInfos.Count <= 0)
            {
                throw new Exception($"获取费用项目【ZZ1001/ZZ1002/ZZ1003/ZZ2301/ZZ0901】时报错：未找到费用项目！");
            }

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.BillNo = item["FBillNo"].ToString();
                cost.OrgId = item["FOrgID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.DeptType = item["FDeptType"].ToString();

                string materialGroup = item["FMaterialGroup"].ToString();
                string billType = item["FBillType"].ToString();

                if (billType.Equals("QTCKD05"))
                {
                    ExpInfo expInfo = expInfos.Where(x => x.ExpNumber.Equals("ZZ0901")).FirstOrDefault();
                    if (expInfo == null)
                    {
                        throw new Exception($"获取其他出库单据类型【QTCKD05】对应的费用项目【ZZ0901】时报错：未找到费用项目！");
                    }
                    cost.ExpID = expInfo.ExpID;
                    cost.ExpNo = expInfo.ExpNumber;
                }
                else
                {

                    if (materialGroup.Equals("01010601") || materialGroup.Equals("01010603"))
                    {
                        ExpInfo expInfo = expInfos.Where(x => x.ExpNumber.Equals("ZZ1003")).FirstOrDefault();
                        if (expInfo == null)
                        {
                            throw new Exception($"获取其他出库物料分组【01010601/01010603】对应的费用项目【ZZ1003】时报错：未找到费用项目！");
                        }
                        cost.ExpID = expInfo.ExpID;
                        cost.ExpNo = expInfo.ExpNumber;
                    }

                    if (materialGroup.Equals("01010602") || materialGroup.Equals("01010604"))
                    {
                        ExpInfo expInfo = expInfos.Where(x => x.ExpNumber.Equals("ZZ1002")).FirstOrDefault();
                        if (expInfo == null)
                        {
                            throw new Exception($"获取其他出库物料分组【01010602/01010604】对应的费用项目【ZZ1002】时报错：未找到费用项目！");
                        }
                        cost.ExpID = expInfo.ExpID;
                        cost.ExpNo = expInfo.ExpNumber;
                    }


                    if (materialGroup.Equals("01010801") || materialGroup.Equals("01010803") || materialGroup.Equals("01010804"))
                    {
                        ExpInfo expInfo = expInfos.Where(x => x.ExpNumber.Equals("ZZ2301")).FirstOrDefault();
                        if (expInfo == null)
                        {
                            throw new Exception($"获取其他出库物料分组【01010801/01010803/01010804】对应的费用项目【ZZ2301】时报错：未找到费用项目！");
                        }
                        cost.ExpID = expInfo.ExpID;
                        cost.ExpNo = expInfo.ExpNumber;
                    }

                    if (materialGroup.Equals("01010802"))
                    {
                        ExpInfo expInfo = expInfos.Where(x => x.ExpNumber.Equals("ZZ1001")).FirstOrDefault();
                        if (expInfo == null)
                        {
                            throw new Exception($"获取其他出库物料分组【01010802】对应的费用项目时【ZZ1001】报错：未找到费用项目！");
                        }
                        cost.ExpID = expInfo.ExpID;
                        cost.ExpNo = expInfo.ExpNumber;
                    }
                }

                if (cost.ExpID == null)
                {
                    throw new Exception($"制造费用分摊不包含【{materialGroup}】分组物料，请核对！");
                }

                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 3.获取折旧调整单的金额
            sql = $@"
                SELECT  A.FBillNo
                       ,A.FOWNERORGID FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,BD.FCostItemID FExpID
                       ,BE.FNumber FExpNo
                       ,SUM(BD.FAllocValue)FAmount
                       ,C.FDEPTID
                       ,D.FNUMBER FDeptType
                       ,BD.F_PDLJ_Base FCustByDept
                       ,ISNULL(DPG.FNUMBER,'') FExpGroup
                  FROM  T_FA_DEPRADJUST A WITH(NOLOCK)
		                INNER JOIN T_FA_DEPRADJUSTENTRY B WITH(NOLOCK)
		                ON A.FID = B.FID
		                INNER JOIN T_FA_DEPRADJUSTDETAIL BD WITH(NOLOCK)
		                ON B.FEntryID = BD.FEntryID
		                INNER JOIN T_BD_Expense BE WITH(NOLOCK)
		                ON BD.FCostItemID = BE.FExpID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON BD.FUseDeptID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_BD_EXPENSE_GROUP DPG WITH(NOLOCK)
                        ON BE.FGROUP = DPG.FID
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门 
                   AND  A.FTransDate >= '{fTOrg.BeginTime}'
                   AND  A.FTransDate < '{fTOrg.EndTime}'
                   AND  A.FOWNERORGID = {fTOrg.OrgId}
                   --AND  BE.FNumber IN ('FYXM08_SYS') --折旧费用
                 GROUP  BY A.FOWNERORGID,C.F_ora_Assistant,C.FDEPTID,BD.FCostItemID,BE.FNumber,D.FNUMBER,BD.F_PDLJ_Base,DPG.FNUMBER,A.FBillNo
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.BillNo = item["FBillNo"].ToString();
                cost.OrgId = item["FOrgID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.DeptType = item["FDeptType"].ToString();
                cost.CustByDept = item["FCustByDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.ExpNo = item["FExpNo"].ToString();
                cost.ExpGroup = item["FExpGroup"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 4.获取费用计提单的金额
            sql = $@"
                SELECT  A.FExpenseOrgId FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,B.FExpID
                       ,BE.FNumber FExpNo
                       ,SUM(B.FTaxSubmitAmt)FAmount
                       ,C.FDEPTID
                       ,D.FNUMBER FDeptType
                       ,B.F_PDLJ_Base FCustByDept
                       ,ISNULL(DPG.FNUMBER,'') FExpGroup
                       ,A.FContactUnitType
                       ,A.FContactUnit
                       ,B.F_ora_Base FProgramID
                       ,A.FBillNo
                  FROM  t_ER_ExpenseJt A WITH(NOLOCK)
		                INNER JOIN t_ER_ExpenseJtEntry B WITH(NOLOCK)
		                ON A.FID = B.FID
		                INNER JOIN T_BD_Expense BE WITH(NOLOCK)
		                ON B.FExpID = BE.FExpID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON B.FExpenseDeptEntryID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_BD_EXPENSE_GROUP DPG WITH(NOLOCK)
                        ON BE.FGROUP = DPG.FID
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门  
                   AND  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FExpenseOrgId = {fTOrg.OrgId}
                   AND  ISNULL(A.FBXBillNo,'') = '' --未下推费用报销单
                   AND  A.FBillNo NOT LIKE '%-%'
                 GROUP  BY A.FExpenseOrgId,C.F_ora_Assistant,C.FDEPTID,B.FExpID,BE.FNumber,D.FNUMBER,B.F_PDLJ_Base,DPG.FNUMBER,A.FContactUnitType,A.FContactUnit,B.F_ora_Base,A.FBillNo
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.BillNo = item["FBillNo"].ToString();
                cost.OrgId = item["FOrgID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.DeptType = item["FDeptType"].ToString();
                cost.CustByDept = item["FCustByDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.ExpNo = item["FExpNo"].ToString();
                cost.ContactUnitType = item["FContactUnitType"].ToString();
                cost.ContactUnit = item["FContactUnit"].ToString();
                cost.ExpGroup = item["FExpGroup"].ToString();
                cost.ProgramID = item["FProgramID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 5.获取费用报销单的金额
            sql = $@"
                SELECT FOrgID,FZYDept,FExpID,FExpNo,FDEPTID,FDeptType,FCustByDept,FExpGroup,FContactUnitType,FContactUnit,FProgramID,SUM(FAmount)FAmount,FBillNo
                  FROM  (
                    SELECT  A.FOrgID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                           ,B.FExpID
                           ,BE.FNumber FExpNo
                           ,CASE WHEN B.FInvoiceType = '0' THEN B.FExpenseAmount ELSE B.FTaxSubmitAmt END FAmount
                           ,C.FDEPTID
                           ,D.FNUMBER FDeptType
                           ,B.F_PDLJ_Base FCustByDept
                           ,ISNULL(DPG.FNUMBER,'') FExpGroup
                           ,A.FContactUnitType
                           ,A.FContactUnit
                           ,B.F_ora_Base FProgramID
                           ,A.FBillNo
                      FROM  T_ER_EXPENSEREIMB A WITH(NOLOCK)
						    INNER JOIN T_ER_EXPENSEREIMBENTRY B WITH(NOLOCK)
						    ON A.FID = B.FID 
		                    INNER JOIN T_BD_Expense BE WITH(NOLOCK)
		                    ON B.FExpID = BE.FExpID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON B.FExpenseDeptEntryID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_BD_EXPENSE_GROUP DPG WITH(NOLOCK)
                            ON BE.FGROUP = DPG.FID
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门 
                       AND  A.FDate >= '{fTOrg.BeginTime}'
                       AND  A.FDate < '{fTOrg.EndTime}'
                       AND  A.FOrgID = {fTOrg.OrgId}
                       AND  (A.FJTDate IS NULL OR (A.FJTDate IS NOT NULL AND YEAR(A.FDate) = YEAR(A.FJTDate) AND MONTH(A.FDate) = MONTH(A.FJTDate)))--计提日期为空
                   ) A                 
                GROUP  BY FOrgID,FZYDept,FExpID,FExpNo,FDEPTID,FDeptType,FCustByDept,FExpGroup,FContactUnitType,FContactUnit,FProgramID,FBillNo
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.BillNo = item["FBillNo"].ToString();
                cost.OrgId = item["FOrgID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.DeptType = item["FDeptType"].ToString();
                cost.CustByDept = item["FCustByDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.ExpNo = item["FExpNo"].ToString();
                cost.ContactUnitType = item["FContactUnitType"].ToString();
                cost.ContactUnit = item["FContactUnit"].ToString();
                cost.ExpGroup = item["FExpGroup"].ToString();
                cost.ProgramID = item["FProgramID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            foreach (var deptCost in deptCostList)
            {
                Cost cost = costList.Where(x => x.OrgId == deptCost.OrgId
                                             && x.IsZY == deptCost.IsZY
                                             && x.ExpID == deptCost.ExpID
                                             && x.DeptType == deptCost.DeptType
                                             && x.CustByDept == deptCost.CustByDept
                                             && x.ExpGroup == deptCost.ExpGroup
                                             && x.ContactUnitType == deptCost.ContactUnitType
                                             && x.ContactUnit == deptCost.ContactUnit
                                             && x.ProgramID == deptCost.ProgramID
                ).FirstOrDefault();

                if (cost == null)
                {
                    costList.Add(new Cost()
                    {
                        OrgId = deptCost.OrgId,
                        IsZY = deptCost.IsZY,
                        ExpID = deptCost.ExpID,
                        ExpNo = deptCost.ExpNo,
                        DeptType = deptCost.DeptType,
                        CustByDept = deptCost.CustByDept,
                        ExpGroup = deptCost.ExpGroup,
                        ContactUnitType = deptCost.ContactUnitType,
                        ContactUnit = deptCost.ContactUnit,
                        ProgramID = deptCost.ProgramID,
                        Amount = deptCost.Amount
                    });
                }
                else
                {
                    cost.Amount = cost.Amount + deptCost.Amount;
                }
            }

            if (costList.Count <= 0)
            {
                throw new Exception("未获取到对应期间的总成本!");
            }

        }

        private void GetDifShareData(FTOrg fTOrg)
        {
            DynamicObjectCollection data = null;
            string sql = "";

            #region 1.获取费用报销单计提日期不为空的差额
            sql = $@"
                SELECT  A.FOrgID FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,B.FExpID
                       ,BE.FNumber FExpNo
                       ,SUM(B.FDifAmount) FAmount
                       ,C.FDEPTID
                       ,D.FNUMBER FDeptType
                       ,B.F_PDLJ_Base FCustByDept
                       ,ISNULL(DPG.FNUMBER,'') FExpGroup
                       ,A.FContactUnitType
                       ,A.FContactUnit
                       ,B.F_ora_Base FProgramID
                       ,A.FBillNo
                  FROM  t_ER_ExpenseReimb A WITH(NOLOCK)
                        INNER JOIN t_ER_ExpenseReimbEntry B WITH(NOLOCK)
                        ON A.FID = B.FID
		                INNER JOIN T_BD_Expense BE WITH(NOLOCK)
		                ON B.FExpID = BE.FExpID
                        INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
	                    ON B.FExpenseDeptEntryID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_BD_EXPENSE_GROUP DPG WITH(NOLOCK)
                        ON BE.FGROUP = DPG.FID
                 WHERE  A.FJTDate >= '{fTOrg.JTBeginTime}'
                   AND  A.FJTDate < '{fTOrg.JTEndTime}'
                   AND  A.FDocumentStatus = 'C'
                   AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门 
                   AND  A.FOrgID = {fTOrg.OrgId}
                   AND  B.FDifAmount <> 0
                 GROUP  BY A.FOrgID,C.F_ora_Assistant,C.FDEPTID,B.FExpID,BE.FNumber,D.FNUMBER,B.F_PDLJ_Base,DPG.FNUMBER,A.FContactUnitType,A.FContactUnit,B.F_ora_Base,A.FBillNo
                ";

            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.OrgId = item["FOrgID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.DeptType = item["FDeptType"].ToString();
                cost.CustByDept = item["FCustByDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.ExpNo = item["FExpNo"].ToString();
                cost.ExpGroup = item["FExpGroup"].ToString();
                cost.ContactUnitType = item["FContactUnitType"].ToString();
                cost.ContactUnit = item["FContactUnit"].ToString();
                cost.ProgramID = item["FProgramID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 2.费用计提有报销单号但报销单中没有对应费用项目的，取费用计提单对应费用项目的负数费用金额
            sql = $@"
                SELECT  A.FExpenseOrgId FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,B.FExpID
                       ,BE.FNumber FExpNo
                       ,SUM(0 - B.FTaxSubmitAmt)FAmount
                       ,C.FDEPTID
                       ,D.FNUMBER FDeptType
                       ,B.F_PDLJ_Base FCustByDept
                       ,ISNULL(DPG.FNUMBER,'') FExpGroup
                       ,A.FContactUnitType
                       ,A.FContactUnit
                       ,B.F_ora_Base FProgramID
                       ,A.FBillNo
                  FROM  t_ER_ExpenseJt A WITH(NOLOCK)
		                INNER JOIN t_ER_ExpenseJtEntry B WITH(NOLOCK)
		                ON A.FID = B.FID
                        LEFT JOIN  (SELECT A.FBillNo,B.FExpID
                                      FROM t_ER_ExpenseReimb A WITH(NOLOCK)
                                           INNER JOIN t_ER_ExpenseReimbEntry B WITH(NOLOCK)
                                           ON A.FID = B.FID
                                     GROUP BY A.FBillNo,B.FExpID )T1
                        ON A.FBXBillNo = T1.FBillNo AND B.FExpID = T1.FExpID
		                INNER JOIN T_BD_Expense BE WITH(NOLOCK)
		                ON B.FExpID = BE.FExpID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON B.FExpenseDeptEntryID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_BD_EXPENSE_GROUP DPG WITH(NOLOCK)
                        ON BE.FGROUP = DPG.FID
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER IN ('DP05_SYS','DP07_SYS') --制造部门,驻点部门  
                   AND  A.FDate >= '{fTOrg.JTBeginTime}'
                   AND  A.FDate < '{fTOrg.JTEndTime}'
                   AND  A.FExpenseOrgId = {fTOrg.OrgId}
                   AND  ISNULL(A.FBXBillNo,'') <> '' --费用报销单号不为空
                   AND  T1.FExpID IS NULL
                 GROUP  BY A.FExpenseOrgId,C.F_ora_Assistant,C.FDEPTID,B.FExpID,BE.FNumber,D.FNUMBER,B.F_PDLJ_Base,DPG.FNUMBER,A.FContactUnitType,A.FContactUnit,B.F_ora_Base,A.FBillNo
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.BillNo = item["FBillNo"].ToString();
                cost.OrgId = item["FOrgID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.DeptType = item["FDeptType"].ToString();
                cost.CustByDept = item["FCustByDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.ExpNo = item["FExpNo"].ToString();
                cost.ExpGroup = item["FExpGroup"].ToString();
                cost.ContactUnitType = item["FContactUnitType"].ToString();
                cost.ContactUnit = item["FContactUnit"].ToString();
                cost.ProgramID = item["FProgramID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }

            #endregion

            foreach (var deptCost in deptCostList)
            {
                Cost cost = costList.Where(x => x.OrgId == deptCost.OrgId
                                             && x.CostCenterId == deptCost.CostCenterId
                                             && x.IsZY == deptCost.IsZY
                                             && x.ExpID == deptCost.ExpID
                                             && x.DeptType == deptCost.DeptType
                                             && x.CustByDept == deptCost.CustByDept
                                             && x.ExpGroup == deptCost.ExpGroup
                                             && x.ContactUnitType == deptCost.ContactUnitType
                                             && x.ContactUnit == deptCost.ContactUnit
                                             && x.ProgramID == deptCost.ProgramID
                ).FirstOrDefault();

                if (cost == null)
                {
                    costList.Add(new Cost()
                    {
                        OrgId = deptCost.OrgId,
                        CostCenterId = deptCost.CostCenterId,
                        IsZY = deptCost.IsZY,
                        ExpID = deptCost.ExpID,
                        ExpNo = deptCost.ExpNo,
                        DeptType = deptCost.DeptType,
                        CustByDept = deptCost.CustByDept,
                        ExpGroup = deptCost.ExpGroup,
                        ContactUnitType = deptCost.ContactUnitType,
                        ContactUnit = deptCost.ContactUnit,
                        ProgramID = deptCost.ProgramID,
                        Amount = deptCost.Amount
                    });
                }
                else
                {
                    cost.Amount = cost.Amount + deptCost.Amount;
                }
            }

            if (costList.Count <= 0)
            {
                throw new Exception($"未获取到对应期间【{fTOrg.JTYear}-{fTOrg.JTPeriod}】的总成本!");
            }
        }

        #region 成本安装数量比例进行分摊

        #endregion

        /// <summary>
        /// 获取分摊时的数量
        /// </summary>
        /// <param name="fTOrg"></param>
        /// <param name="isDif"></param>
        private void GetNum(FTOrg fTOrg, bool isDif = false)
        {

            custNumList.Clear();

            //1.如果组织是伊莱亚，数量从暂估应收中获取
            //2.如果组织不是伊莱亚，数量从分摊录入中获取

            string sql = "";
            DynamicObjectCollection data = null;

            #region 1.从销售订单获取数量
            //        if (fTOrg.OrgId.Contains("100119"))
            //        {
            //            sql = $@"
            //SELECT FOrgID,FZYDept,FCustID,FCostCenterId,SUM(FWeight)FWeight,SUM(FQTY)FQTY
            //  FROM  (
            //                SELECT  A.FSaleOrgId FOrgID
            //                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
            //                       ,A.FCustID
            //                       ,B.FMaterialID
            //                       ,ISNULL(E.FCostCenterId,'')FCostCenterId
            //                       ,B.F_ora_Qty FWeight
            //                       ,B.F_ora_Qty1 FQty
            //                  FROM  T_SAL_ORDER A WITH(NOLOCK)
            //                  INNER JOIN T_SAL_ORDEREntry B WITH(NOLOCK)
            //                  ON A.FID = B.FID
            //                  INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
            //                  ON A.FSALEDEPTID = C.FDEPTID
            //                  INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
            //                  ON C.FDeptProperty = D.FENTRYID 
            //                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
            //                        ON C.FDEPTID = E.FRELATION
            //                 WHERE  A.FDOCUMENTSTATUS = 'C'
            //                   --AND  D.FNUMBER = 'DP01_SYS' --生产部门 
            //                   AND  A.FDate >= '{fTOrg.BeginTime}'
            //                   AND  A.FDate < '{fTOrg.EndTime}'
            //                   AND  A.FSaleOrgId = {fTOrg.OrgId}
            //                ) A
            //             GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId
            //            ";
            //            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            //            foreach (var item in data)
            //            {
            //                CustNum custNum = new CustNum();
            //                custNum.OrgId = item["FOrgID"].ToString();
            //                custNum.CostCenterId = item["FCostCenterId"].ToString();
            //                custNum.CustID = item["FCustID"].ToString();
            //                //custNum.MaterialID = item["FMaterialID"].ToString();
            //                custNum.IsZY = item["FZYDept"].ToString();
            //                custNum.Weight = Convert.ToDecimal(item["FWeight"]);
            //                custNum.Qty = Convert.ToDecimal(item["FQty"]);
            //                custNumList.Add(custNum);
            //            }
            //        }
            #endregion

            #region 1.从暂估应收获取数量
            if (fTOrg.OrgId.Contains("100119"))
            {
                sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,SUM(FWeight)FWeight,SUM(FQTY)FQTY,FDeptType,FBusinessBillNo
				  FROM  (
                    SELECT  A.FSETTLEORGID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                           ,A.FCUSTOMERID FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                           --,ISNULL(F.FXiXiaoType,'') FXiXiaoType
                           ,B.F_ora_Qty FWeight
                           ,B.F_ora_Qty1 FQty
                           ,D.FNumber FDeptType
                           ,A.FBillNo FBusinessBillNo
                      FROM  T_AR_RECEIVABLE A WITH(NOLOCK)
		                    INNER JOIN T_AR_RECEIVABLEENTRY B WITH(NOLOCK)
		                    ON A.FID = B.FID
						    INNER JOIN T_AR_RECEIVABLEENTRY_O BO WITH(NOLOCK)
		                    ON B.FENTRYID = BO.FENTRYID
                            LEFT JOIN T_AR_RECEIVABLEENTRY_LK BEL WITH(NOLOCK)
                            ON BEL.FSID = B.FENTRYID AND BEL.FSBILLID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON A.FSALEDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                            LEFT JOIN T_BD_MATERIAL F WITH(NOLOCK) --物料
                            ON B.FMaterialID = F.FMaterialID
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP05_SYS' --制造部门 
                       AND  A.FDate >= '{fTOrg.JTBeginTime}'
                       AND  A.FDate < '{fTOrg.JTEndTime}'
                       AND  A.FSETTLEORGID = {fTOrg.OrgId}
                       AND  A.FSetAccountType = 2 --暂估应收
                       --AND  BEL.FSID IS NULL
                       AND  A.FBillNo NOT LIKE '%-%'
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,FDeptType,FBusinessBillNo
                ";
                data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                foreach (var item in data)
                {
                    CustNum custNum = new CustNum();
                    custNum.OrgId = item["FOrgID"].ToString();
                    custNum.CostCenterId = item["FCostCenterId"].ToString();
                    custNum.CustID = item["FCustID"].ToString();
                    custNum.MaterialID = item["FMaterialID"].ToString();
                    custNum.IsZY = item["FZYDept"].ToString();
                    custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                    custNum.Qty = Convert.ToDecimal(item["FQty"]);
                    custNum.DeptType = item["FDeptType"].ToString();
                    custNum.BusinessBillNo = item["FBusinessBillNo"].ToString();
                    custNumList.Add(custNum);
                }
            }
            #endregion

            #region 1.从成本分摊录入获取数量
            if (!fTOrg.OrgId.Contains("100119"))
            {
                sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,SUM(FWeight)FWeight,SUM(FQTY)FQTY,FDeptType,FBusinessBillNo
				  FROM  (
                    SELECT  A.FOrgID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FNumber ELSE '0' END FZYDept
                           ,B.FCustomerId FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                          -- ,ISNULL(F.FXiXiaoType,'') FXiXiaoType
                           ,B.FWEIGHT FWeight
                           ,B.FQTY FQty
                           ,D.FNumber FDeptType
                           ,A.FBillNo FBusinessBillNo
                      FROM  YJ_T_CostShareImport A WITH(NOLOCK)
		                    INNER JOIN YJ_T_CostShareImportEntry B WITH(NOLOCK)
		                    ON A.FID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON B.FDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                            LEFT JOIN T_BD_MATERIAL F WITH(NOLOCK) --物料
                            ON B.FMaterialID = F.FMaterialID
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP05_SYS' --生产部门
                       AND  A.FDate >= '{fTOrg.JTBeginTime}'
                       AND  A.FDate < '{fTOrg.JTEndTime}'
                       AND  A.FOrgID = {fTOrg.OrgId}
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,FDeptType,FBusinessBillNo
                ";
                data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                foreach (var item in data)
                {
                    CustNum custNum = new CustNum();
                    custNum.OrgId = item["FOrgID"].ToString();
                    custNum.CostCenterId = item["FCostCenterId"].ToString();
                    custNum.CustID = item["FCustID"].ToString();
                    custNum.MaterialID = item["FMaterialID"].ToString();
                    custNum.IsZY = item["FZYDept"].ToString();
                    custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                    custNum.Qty = Convert.ToDecimal(item["FQty"]);
                    custNum.DeptType = item["FDeptType"].ToString();
                    custNum.BusinessBillNo = item["FBusinessBillNo"].ToString();
                    custNumList.Add(custNum);
                }
            }

            #endregion

            if (isDif && custNumList.Count <= 0)
            {
                #region 1.从暂估应收获取数量
                if (fTOrg.OrgId.Contains("100119"))
                {
                    sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,SUM(FWeight)FWeight,SUM(FQTY)FQTY,FDeptType,FBusinessBillNo
				  FROM  (
                    SELECT  A.FSETTLEORGID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                           ,A.FCUSTOMERID FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                           --,ISNULL(F.FXiXiaoType,'') FXiXiaoType
                           ,B.F_ora_Qty FWeight
                           ,B.F_ora_Qty1 FQty
                           ,D.FNumber FDeptType
                           ,A.FBillNo FBusinessBillNo
                      FROM  T_AR_RECEIVABLE A WITH(NOLOCK)
		                    INNER JOIN T_AR_RECEIVABLEENTRY B WITH(NOLOCK)
		                    ON A.FID = B.FID
						    INNER JOIN T_AR_RECEIVABLEENTRY_O BO WITH(NOLOCK)
		                    ON B.FENTRYID = BO.FENTRYID
                            LEFT JOIN T_AR_RECEIVABLEENTRY_LK BEL WITH(NOLOCK)
                            ON BEL.FSID = B.FENTRYID AND BEL.FSBILLID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON A.FSALEDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                            LEFT JOIN T_BD_MATERIAL F WITH(NOLOCK) --物料
                            ON B.FMaterialID = F.FMaterialID
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP05_SYS' --制造部门 
                       AND  A.FDate >= '{fTOrg.BeginTime}'
                       AND  A.FDate < '{fTOrg.EndTime}'
                       AND  A.FSETTLEORGID = {fTOrg.OrgId}
                       AND  A.FSetAccountType = 2 --暂估应收
                       AND  BEL.FSID IS NULL
                       AND  A.FBillNo NOT LIKE '%-%'
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,FDeptType,FBusinessBillNo
                ";
                    data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                    foreach (var item in data)
                    {
                        CustNum custNum = new CustNum();
                        custNum.OrgId = item["FOrgID"].ToString();
                        custNum.CostCenterId = item["FCostCenterId"].ToString();
                        custNum.CustID = item["FCustID"].ToString();
                        custNum.MaterialID = item["FMaterialID"].ToString();
                        custNum.IsZY = item["FZYDept"].ToString();
                        custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                        custNum.Qty = Convert.ToDecimal(item["FQty"]);
                        custNum.DeptType = item["FDeptType"].ToString();
                        custNum.BusinessBillNo = item["FBusinessBillNo"].ToString();
                        custNumList.Add(custNum);
                    }
                }
                #endregion

                #region 1.从成本分摊录入获取数量
                if (!fTOrg.OrgId.Contains("100119"))
                {
                    sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,SUM(FWeight)FWeight,SUM(FQTY)FQTY,FDeptType,FBusinessBillNo
				  FROM  (
                    SELECT  A.FOrgID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FNumber ELSE '0' END FZYDept
                           ,B.FCustomerId FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                          -- ,ISNULL(F.FXiXiaoType,'') FXiXiaoType
                           ,B.FWEIGHT FWeight
                           ,B.FQTY FQty
                           ,D.FNumber FDeptType
                           ,A.FBillNo FBusinessBillNo
                      FROM  YJ_T_CostShareImport A WITH(NOLOCK)
		                    INNER JOIN YJ_T_CostShareImportEntry B WITH(NOLOCK)
		                    ON A.FID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON B.FDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                            LEFT JOIN T_BD_MATERIAL F WITH(NOLOCK) --物料
                            ON B.FMaterialID = F.FMaterialID
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP05_SYS' --生产部门
                       AND  A.FDate >= '{fTOrg.BeginTime}'
                       AND  A.FDate < '{fTOrg.EndTime}'
                       AND  A.FOrgID = {fTOrg.OrgId}
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId,FMaterialID,FDeptType,FBusinessBillNo
                ";
                    data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                    foreach (var item in data)
                    {
                        CustNum custNum = new CustNum();
                        custNum.OrgId = item["FOrgID"].ToString();
                        custNum.CostCenterId = item["FCostCenterId"].ToString();
                        custNum.CustID = item["FCustID"].ToString();
                        custNum.MaterialID = item["FMaterialID"].ToString();
                        custNum.IsZY = item["FZYDept"].ToString();
                        custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                        custNum.Qty = Convert.ToDecimal(item["FQty"]);
                        custNum.DeptType = item["FDeptType"].ToString();
                        custNum.BusinessBillNo = item["FBusinessBillNo"].ToString();
                        custNumList.Add(custNum);
                    }
                }

                #endregion
            }

            if (custNumList.Count <= 0)
            {
                throw new Exception($"未获取到对应期间【{fTOrg.JTYear}-{fTOrg.JTPeriod}】的总重量或件数!");
            }
        }

        private void FTDetailCost(FTOrg fTOrg)
        {
            detailCostList.Clear();

            foreach (var cost in costList.Where(x => x.OrgId == fTOrg.OrgId))
            {
                List<CustNum> custNums = new List<CustNum>();

                //制造部门 非独立核算
                if (cost.DeptType == "DP05_SYS" && cost.IsZY.Equals("0"))
                {
                    custNums = custNumList.Where(x => x.OrgId == cost.OrgId && x.DeptType == cost.DeptType).ToList();
                }
                //制造部门 独立核算
                if (cost.DeptType == "DP05_SYS" && !cost.IsZY.Equals("0"))
                {
                    custNums = custNumList.Where(x => x.OrgId == cost.OrgId && x.IsZY == cost.IsZY && x.DeptType == cost.DeptType).ToList();
                }
                //驻点部门
                if (cost.DeptType == "DP07_SYS")
                {
                    custNums = custNumList.Where(x => x.OrgId == cost.OrgId && x.CustID == cost.CustByDept && x.DeptType == cost.DeptType).ToList();
                }

                if (custNums.Count <= 0)
                {
                    continue;
                }

                if (cost.ExpGroup.Equals("ZZ01") || cost.ExpGroup.Equals("ZZ24"))
                {
                    cost.FTByQty = true;
                }

                //人工费按照件数分配
                //非人工费按照吨数分配
                decimal sumQty = cost.FTByQty ? custNums.Sum(x => x.Qty) : custNums.Sum(x => x.Weight);
                decimal sharyAmount = 0;
                decimal sumSharyAmount = 0;

                int count = 0;

                foreach (var custNum in custNums)
                {
                    if (count != custNums.Count - 1)
                    {
                        sharyAmount = Math.Round(cost.Amount * ((cost.FTByQty ? custNum.Qty : custNum.Weight) / sumQty), 2);
                    }
                    else
                    {
                        sharyAmount = cost.Amount - sumSharyAmount;
                    }

                    DetailCost detailCost = new DetailCost
                    {
                        OrgId = cost.OrgId,
                        CostCenterId = cost.CostCenterId,
                        IsZY = cost.IsZY,
                        CustID = custNum.CustID,
                        MaterialID = custNum.MaterialID,
                        ExpID = cost.ExpID,
                        ExpNo = cost.ExpNo,
                        Amount = sharyAmount,
                        DeptID = cost.DeptID,
                        BusinessBillNo = custNum.BusinessBillNo,
                        Weight = custNum.Weight,
                        Qty = custNum.Qty,
                        ProgramID = cost.ProgramID
                    };

                    sumSharyAmount = sumSharyAmount + sharyAmount;

                    detailCostList.Add(detailCost);

                    count++;
                }

            }
        }

        void CreateCostShare(FTOrg fTOrg, string billType = "CBFT1")
        {
            // 构建一个IBillView实例，通过此实例，可以方便的填写物料各属性
            IBillView billView = this.CreateBillView();
            // 新建一个空白物料
            // billView.CreateNewModelData();
            ((IBillViewService)billView).LoadData();

            // 触发插件的OnLoad事件：
            // 组织控制基类插件，在OnLoad事件中，对主业务组织改变是否提示选项进行初始化。
            // 如果不触发OnLoad事件，会导致主业务组织赋值不成功
            DynamicFormViewPlugInProxy eventProxy = billView.GetService<DynamicFormViewPlugInProxy>();
            eventProxy.FireOnLoad();

            this.FillBillPropertysByCostShare(billView, fTOrg, billType);
            OperateOption saveOption = OperateOption.Create();
            shareResultList.Add(SaveBill(fTOrg, billView, saveOption));
        }

        /// <summary>
        /// 把物料的各属性，填写到IBillView当前所管理的物料中
        /// </summary>
        /// <param name="billView"></param>
        private void FillBillPropertysByCostShare(IBillView billView, FTOrg fTOrg, string billType)
        {
            int index = 0;

            // 调用IDynamicFormViewService.UpdateValue: 会执行字段的值更新事件
            // 调用 dynamicFormView.SetItemValueByNumber ：不会执行值更新事件，需要继续调用：
            // ((IDynamicFormView)dynamicFormView).InvokeFieldUpdateService(key, rowIndex);
            IDynamicFormViewService dynamicFormView = billView as IDynamicFormViewService;
            dynamicFormView.SetItemValueByID("FOrgId", fTOrg.OrgId, index);
            dynamicFormView.SetItemValueByNumber("FBillTypeId", billType, index);
            dynamicFormView.UpdateValue("FYear", index, fTOrg.Year);
            dynamicFormView.UpdateValue("FPeriod", index, fTOrg.Period);
            dynamicFormView.UpdateValue("FDate", index, fTOrg.BeginTime);
            dynamicFormView.UpdateValue("FJTYear", index, fTOrg.JTYear);
            dynamicFormView.UpdateValue("FJTPeriod", index, fTOrg.JTPeriod);

            //分摊明细单据体
            billView.Model.BatchCreateNewEntryRow("FEntity", detailCostList.Count);
            foreach (var detailCost in detailCostList.OrderByDescending(x => x.CustID))
            {
                dynamicFormView.SetItemValueByID("FCustomerId", detailCost.CustID, index);
                dynamicFormView.SetItemValueByID("FCostCenter", detailCost.CostCenterId, index);
                dynamicFormView.SetItemValueByID("FExpense", detailCost.ExpID, index);
                dynamicFormView.SetItemValueByID("FMaterialId", detailCost.MaterialID, index);
                dynamicFormView.SetItemValueByID("FEntryDeptID", detailCost.DeptID, index);
                dynamicFormView.SetItemValueByID("FProgramID", detailCost.ProgramID, index);
                dynamicFormView.UpdateValue("FBusinessBillNo", index, detailCost.BusinessBillNo);
                dynamicFormView.UpdateValue("FAmount", index, detailCost.Amount);
                dynamicFormView.UpdateValue("FWeight", index, detailCost.Weight);
                dynamicFormView.UpdateValue("FQty", index, detailCost.Qty);

                index++;
            }

            //制造费用单据体
            index = 0;
            billView.Model.BatchCreateNewEntryRow("FSumAmountEntry", deptCostList.Count);
            foreach (var deptCost in deptCostList.OrderByDescending(x => x.DeptID))
            {
                dynamicFormView.UpdateValue("FSourceBillNo", index,deptCost.BillNo);
                dynamicFormView.SetItemValueByID("FDeptID", deptCost.DeptID, index);
                dynamicFormView.SetItemValueByID("FExpenseID", deptCost.ExpID, index);
                dynamicFormView.SetItemValueByID("FSumCustID", deptCost.CustByDept, index);
                dynamicFormView.UpdateValue("FSumContactUnitType", index, deptCost.ContactUnitType);
                dynamicFormView.SetItemValueByID("FSumContactUnit", deptCost.ContactUnit, index);
                dynamicFormView.SetItemValueByID("FSumProgramID", deptCost.ProgramID, index);
                dynamicFormView.UpdateValue("FExpAmount", index, deptCost.Amount);

                index++;
            }
        }

        /// <summary>
        /// 获取需要进行分摊的组织和期间
        /// </summary>
        /// <exception cref="KDException"></exception>
        private void GetSelectData()
        {
            fTOrgList.Clear();

            DynamicObject billObj = this.Model.DataObject;
            DynamicObjectCollection queryEntrys = billObj["FQueryEntity"] as DynamicObjectCollection;

            foreach (var queryEntry in queryEntrys)
            {
                if (!Convert.ToBoolean(queryEntry["FChecked"]))
                {
                    continue;
                }

                DynamicObject org = queryEntry["FOrgId"] as DynamicObject;
                DynamicObject acctPolicy = queryEntry["FAcctPolicy"] as DynamicObject;

                if (org == null)
                {
                    throw new KDException("", "组织不能为空");
                }

                if (acctPolicy == null)
                {
                    throw new KDException("", "会计政策不能为空");
                }

                if (queryEntry["FYear"] == null)
                {
                    throw new KDException("", "年度不能为空");
                }

                if (queryEntry["FPeriod"] == null)
                {
                    throw new KDException("", "期间不能为空");
                }

                FTOrg fTOrg = new FTOrg();
                fTOrg.OrgId = org["Id"].ToString();
                fTOrg.AcctPolicy = acctPolicy["Id"].ToString();
                fTOrg.Year = queryEntry["FYear"].ToString();
                fTOrg.Period = queryEntry["FPeriod"].ToString();
                fTOrg.JTYear = queryEntry["FYear"].ToString();
                fTOrg.JTPeriod = queryEntry["FPeriod"].ToString();

                fTOrg.BeginTime = $@"{fTOrg.Year}-{fTOrg.Period}-01";
                fTOrg.EndTime = Convert.ToDateTime(fTOrg.BeginTime).AddMonths(1).ToString("yyyy-MM-dd");
                fTOrg.JTBeginTime = $@"{fTOrg.Year}-{fTOrg.Period}-01";
                fTOrg.JTEndTime = Convert.ToDateTime(fTOrg.BeginTime).AddMonths(1).ToString("yyyy-MM-dd");

                fTOrgList.Add(fTOrg);
            }
        }

        private void ShowResult()
        {
            DynamicObjectCollection resultEntrys = this.View.Model.DataObject["FResultEntity"] as DynamicObjectCollection;
            resultEntrys.Clear();

            foreach (var shareResult in shareResultList)
            {
                BaseDataField fldOrg = this.View.BillBusinessInfo.GetField("FROrgId") as BaseDataField;
                BaseDataField fldAcctPolicy = this.View.BillBusinessInfo.GetField("FRAcctPolicy") as BaseDataField;
                

                DynamicObject resultEntry = new DynamicObject(resultEntrys.DynamicCollectionItemPropertyType);
              
                resultEntry["FRYear"] = shareResult.Org.Year;
                resultEntry["FRPeriod"] = shareResult.Org.Period;
                resultEntry["FRStatus"] = shareResult.Status;
                resultEntry["FRMessage"] = shareResult.Message;


                DynamicObject[] orgObjs = BusinessDataServiceHelper.LoadFromCache(this.Context,new object[] { shareResult.Org.OrgId },fldOrg.RefFormDynamicObjectType);
                fldOrg.RefIDDynamicProperty.SetValue(resultEntry, orgObjs[0][0]);
                fldOrg.DynamicProperty.SetValue(resultEntry, orgObjs[0]);

                DynamicObject[] acctPolicyObjs = BusinessDataServiceHelper.LoadFromCache(this.Context, new object[] { shareResult.Org.AcctPolicy }, fldAcctPolicy.RefFormDynamicObjectType);
                fldAcctPolicy.RefIDDynamicProperty.SetValue(resultEntry, acctPolicyObjs[0][0]);
                fldAcctPolicy.DynamicProperty.SetValue(resultEntry, acctPolicyObjs[0]);

                resultEntrys.Add(resultEntry);
            }

            this.View.UpdateView("FResultEntity");
        }

        /// <summary>
        /// 保存操作，并显示保存结果
        /// </summary>
        /// <param name="billView"></param>
        /// <param name="saveOption"></param>
        private ShareResult SaveBill(FTOrg fTOrg,IBillView billView, OperateOption saveOption)
        {
            // 设置FormId
            Form form = billView.BillBusinessInfo.GetForm();
            if (form.FormIdDynamicProperty != null)
            {
                form.FormIdDynamicProperty.SetValue(billView.Model.DataObject, form.Id);
            }

            // 调用保存操作
            IOperationResult saveResult = BusinessDataServiceHelper.Save(
            this.Context,
            billView.BillBusinessInfo,
            billView.Model.DataObject,
            saveOption,
            "Save");

            string status = "";
            string message = "";

            // 显示处理结果
            if (saveResult == null)
            {
                status = "失败";
                message = "未知原因导致生成成本分摊单失败!";
            }
            else if (saveResult.IsSuccess == true)
            {
                status = "成功";
                foreach (var item in saveResult.OperateResult)
                {
                    message = message + item.Message;
                }

            }
            else if (saveResult.IsSuccess == false)
            {
                status = "失败";
                foreach (var item in saveResult.ValidationErrors)
                {
                    message = message + item.Message;
                }
            }

            return new ShareResult { Org = fTOrg, Status = status, Message = message };
           
        }

        /// <summary>
        /// 创建单据视图
        /// </summary>
        /// <returns></returns>
        private IBillView CreateBillView()
        {
            // 读取物料的元数据
            FormMetadata meta = MetaDataServiceHelper.Load(this.Context, "PZXD_CostShare") as FormMetadata;
            Form form = meta.BusinessInfo.GetForm();
            // 创建用于引入数据的单据view
            Type type = Type.GetType("Kingdee.BOS.Web.Import.ImportBillView,Kingdee.BOS.Web");
            var billView = (IDynamicFormViewService)Activator.CreateInstance(type);
            // 开始初始化billView：
            // 创建视图加载参数对象，指定各种参数，如FormId, 视图(LayoutId)等
            BillOpenParameter openParam = CreateOpenParameter(meta);
            // 动态领域模型服务提供类，通过此类，构建MVC实例
            var provider = form.GetFormServiceProvider();
            billView.Initialize(openParam, provider);
            return billView as IBillView;
        }

        /// <summary>
        /// 创建视图加载参数对象，指定各种初始化视图时，需要指定的属性
        /// </summary>
        /// <param name="meta"></param>
        /// <returns></returns>
        private BillOpenParameter CreateOpenParameter(FormMetadata meta)
        {
            Form form = meta.BusinessInfo.GetForm();
            // 指定FormId, LayoutId
            BillOpenParameter openParam = new BillOpenParameter(form.Id, meta.GetLayoutInfo().Id);
            // 数据库上下文
            openParam.Context = this.Context;
            // 本单据模型使用的MVC框架
            openParam.ServiceName = form.FormServiceName;
            // 随机产生一个不重复的PageId，作为视图的标识
            openParam.PageId = Guid.NewGuid().ToString();
            // 元数据
            openParam.FormMetaData = meta;
            // 界面状态：新增 (修改、查看)
            openParam.Status = OperationStatus.ADDNEW;
            // 单据主键：本案例演示新建物料，不需要设置主键
            openParam.PkValue = null;
            // 界面创建目的：普通无特殊目的 （为工作流、为下推、为复制等）
            openParam.CreateFrom = CreateFrom.Default;
            // 基础资料分组维度：基础资料允许添加多个分组字段，每个分组字段会有一个分组维度
            // 具体分组维度Id，请参阅 form.FormGroups 属性
            openParam.GroupId = "";
            // 基础资料分组：如果需要为新建的基础资料指定所在分组，请设置此属性
            openParam.ParentId = 0;
            // 单据类型
            openParam.DefaultBillTypeId = "";
            // 业务流程
            openParam.DefaultBusinessFlowId = "";
            // 主业务组织改变时，不用弹出提示界面
            openParam.SetCustomParameter("ShowConfirmDialogWhenChangeOrg", false);
            // 插件
            List<AbstractDynamicFormPlugIn> plugs = form.CreateFormPlugIns();
            openParam.SetCustomParameter(FormConst.PlugIns, plugs);
            PreOpenFormEventArgs args = new PreOpenFormEventArgs(this.Context, openParam);
            foreach (var plug in plugs)
            {// 触发插件PreOpenForm事件，供插件确认是否允许打开界面
                plug.PreOpenForm(args);
            }
            if (args.Cancel == true)
            {// 插件不允许打开界面
             // 本案例不理会插件的诉求，继续....
            }
            // 返回
            return openParam;
        }

    }

    //需要分摊的组织
    public class FTOrg
    {
        public string OrgId { get; set; }

        public string AcctPolicy { get; set; }

        public string Year { get; set; }

        public string Period { get; set; }

        public string BeginTime { get; set; }

        public string EndTime { get; set; }

        public string JTYear { get; set; }

        public string JTPeriod { get; set; }

        public string JTBeginTime { get; set; }

        public string JTEndTime { get; set; }

    }

    //总成本
    public class Cost
    {
        /// <summary>
        /// 组织
        /// </summary>
        public string OrgId { get; set; }

        /// <summary>
        /// 成本中心
        /// </summary>
        public string CostCenterId { get; set; }

        /// <summary>
        /// 专用车间
        /// </summary>
        public string IsZY { get; set; }

        /// <summary>
        /// 部门，获取明细总成本的时候才使用
        /// </summary>
        public string DeptID { get; set; }

        /// <summary>
        /// 部门属性 制造部门/驻点部门
        /// </summary>
        public string DeptType { get; set; }

        /// <summary>
        /// 费用分组
        /// </summary>
        public string ExpGroup { get; set; } = "";

        /// <summary>
        /// 按件数分摊
        /// </summary>
        public bool FTByQty { get; set; } = false;

        /// <summary>
        /// 驻点部门对应客户
        /// </summary>
        public string CustByDept { get; set; } = "";

        /// <summary>
        /// 费用项目
        /// </summary>
        public string ExpID { get; set; }

        /// <summary>
        /// 往来单位类型
        /// </summary>
        public string ContactUnitType { get; set; } = "";

        /// <summary>
        /// 往来单位
        /// </summary>
        public string ContactUnit { get; set; } = "0";

        /// <summary>
        /// 项目号
        /// </summary>
        public string ProgramID { get; set; } = "0";

        public string ExpNo { get; set; }

        /// <summary>
        /// 金额
        /// </summary>
        public decimal Amount = 0;

        public string BillNo  { get; set; }
}

    //每个客户每个产品的数量
    public class CustNum
    {
        /// <summary>
        /// 组织
        /// </summary>
        public string OrgId { get; set; }

        public string DeptType { get; set; }

        /// <summary>
        /// 成本中心
        /// </summary>
        public string CostCenterId { get; set; }

        public string IsZY { get; set; }

        public string CustID { get; set; }

        public string MaterialID { get; set; }

        public string XiXiaoType { get; set; }


        public decimal Weight { get; set; }

        public decimal Qty { get; set; }

        /// <summary>
        /// 业务单号
        /// </summary>
        public string BusinessBillNo { get; set; }
    }

    //每个组织每个费用项目每个成本中心每个客户每个产品的成本
    public class DetailCost
    {
        public string OrgId { get; set; }

        public string CostCenterId { get; set; }

        public string ExpID { get; set; }

        public string ExpNo { get; set; }

        /// <summary>
        /// 专用车间
        /// </summary>
        public string IsZY { get; set; }

        public string CustID { get; set; }

        public string MaterialID { get; set; }

        public string XiXiaoType { get; set; }

        public decimal Amount { get; set; }

        public string DeptID { get; set; }

        /// <summary>
        /// 业务单号
        /// </summary>
        public string BusinessBillNo { get; set; }

        /// <summary>
        /// 吨数
        /// </summary>
        public decimal Weight { get; set; }

        /// <summary>
        /// 件数
        /// </summary>
        public decimal Qty { get; set; }

        /// <summary>
        /// 项目号
        /// </summary>
        public string ProgramID { get; set; } = "0";
    }

    public class ShareResult
    {
        public FTOrg Org { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }
    }

    public class ExpInfo
    {
        public string ExpID { get; set; }

        public string ExpNumber { get; set; }
    }
}
