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
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

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
        List<string> expList = new List<string>();
        List<FTOrg> fTOrgList = new List<FTOrg>();
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
                    shareResultList.Add(new ShareResult { Org = fTOrg, Status = "失败", Message = ex.Message });
                }

            }
            //GetDifShareData()
        }

        #region 费用分摊
        private void ExpFT(FTOrg fTOrg)
        {
            //获取费用项目
            GetExpId(fTOrg);
            //获取总成本
            GetSumCost(fTOrg);
            //获取数量
            GetNum(fTOrg);
            //总成本分摊到客户产品
            FTDetailCost(fTOrg);
            //创建成本分摊单
            CreateCostShare(fTOrg);
        }

        /// <summary>
        /// 动态获取费用科目：获取对应期间且部门为生产部门的计提单上的所有费用项目
        /// </summary>
        /// <param name="fTOrg"></param>
        private List<string> GetExpId(FTOrg fTOrg)
        {
            expList.Clear();

            string sql = $@"
                SELECT  DISTINCT B.FExpID
                  FROM  t_ER_ExpenseJt A WITH(NOLOCK)
		                INNER JOIN t_ER_ExpenseJtEntry B WITH(NOLOCK)
		                ON A.FID = B.FID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK)
		                ON A.FExpenseDeptID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK)
		                ON C.FDeptProperty = D.FENTRYID 
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                   AND  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FExpenseOrgId = {fTOrg.OrgId}
                ";
            DynamicObjectCollection data
                = DBUtils.ExecuteDynamicObject(this.Context, sql);
            expList = data.Select(x => x["FExpID"].ToString()).ToList();
            if (expList == null)
            {
                expList = new List<string>();
            }

            expList.Add("111685");//化料成本
            expList.Add("111686");//辅料成本

            return expList.Distinct().ToList();
        }

        private void GetSumCost(FTOrg fTOrg)
        {
            //1.化料成本，辅料成本的费用项目：
            //如果有财务应付，则获取财务应付的金额
            //如果没有财务应付，则获取暂估应付的金额
            //2.非化料成本，辅料成本的费用项目：
            //如果费用计提单没有下推费用报销单，则获取费用计提单的金额
            //如果费用计提单已经下推费用报销单，则获取费用报销单的金额
            //3.获取金额的时候，需要根据车间进行分组获取金额，车间分为专用车间和非专用车间。
            //例：专用车间的成本只分摊专用车间的客户。非专用车间的成本只分摊非专用车间的客户

            deptCostList.Clear();
            costList.Clear();

            string sql = "";
            DynamicObjectCollection data = null;

            #region 1.获取财务应付的金额
            sql = $@"
                SELECT  BLH.FSETTLEORGID FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,BL.FCOSTID FExpID
                       ,ISNULL(E.FCostCenterID,0) FCostCenterID
                       ,SUM(BL.FALLAMOUNTFOR)FAmount
                       ,C.FDEPTID
                  FROM  T_AP_PAYABLE A WITH(NOLOCK)
		                INNER JOIN T_AP_PAYABLEENTRY B WITH(NOLOCK)
		                ON A.FID = B.FID
                        LEFT JOIN T_AP_PAYABLE_LK BEL WITH(NOLOCK)
                        ON BEL.FSID = B.FENTRYID AND BEL.FSBILLID = B.FID
                        LEFT JOIN T_AP_PAYABLEENTRY BL WITH(NOLOCK)
                        ON BEL.FENTRYID = BL.FENTRYID
                        LEFT JOIN T_AP_PAYABLE BLH WITH(NOLOCK)
                        ON BL.FID = BLH.FID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON BLH.FPURCHASEDEPTID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                        ON C.FDEPTID = E.FRELATION
                 WHERE  BLH.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                   AND  BLH.FDate >= '{fTOrg.BeginTime}'
                   AND  BLH.FDate < '{fTOrg.EndTime}'
                   AND  BLH.FSETTLEORGID = {fTOrg.OrgId}
                   AND  A.FSetAccountType = 2 --暂估应付
                   AND  BLH.FSetAccountType = 3 --财务应付
                   AND  BL.FCOSTID IN (111685,111686) --化料成本，辅料成本
                 GROUP  BY BLH.FSETTLEORGID,C.F_ora_Assistant,C.FDEPTID,BL.FCOSTID,E.FCostCenterID --专用车间,费用项目
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.OrgId = item["FOrgID"].ToString();
                cost.CostCenterId = item["FCostCenterID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 2.获取暂估应付的金额
            sql = $@"
                SELECT  A.FSETTLEORGID FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,B.FCOSTID FExpID
                       ,ISNULL(E.FCostCenterID,0) FCostCenterID
                       ,SUM(B.FALLAMOUNTFOR)FAmount
                       ,C.FDEPTID
                  FROM  T_AP_PAYABLE A WITH(NOLOCK)
		                INNER JOIN T_AP_PAYABLEENTRY B WITH(NOLOCK)
		                ON A.FID = B.FID
                        LEFT JOIN T_AP_PAYABLE_LK BEL WITH(NOLOCK)
                        ON BEL.FSID = B.FENTRYID AND BEL.FSBILLID = B.FID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON A.FPURCHASEDEPTID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                        ON C.FDEPTID = E.FRELATION
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                   AND  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FSETTLEORGID = {fTOrg.OrgId}
                   AND  A.FSetAccountType = 2 --暂估应付
                   AND  BEL.FSID IS NULL
                   AND  A.FBillNo NOT LIKE '%-%'
                   AND  B.FCOSTID IN (111685,111686) --化料成本，辅料成本
                 GROUP  BY A.FSETTLEORGID,C.F_ora_Assistant,C.FDEPTID,B.FCOSTID,E.FCostCenterID --专用车间,费用项目
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.OrgId = item["FOrgID"].ToString();
                cost.CostCenterId = item["FCostCenterID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 3.获取费用计提单的金额
            sql = $@"
                SELECT  A.FExpenseOrgId FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,B.FExpID
                       ,ISNULL(E.FCostCenterID,0) FCostCenterID
                       ,SUM(B.FExpenseAmount)FAmount
                       ,C.FDEPTID
                  FROM  t_ER_ExpenseJt A WITH(NOLOCK)
		                INNER JOIN t_ER_ExpenseJtEntry B WITH(NOLOCK)
		                ON A.FID = B.FID
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON A.FExpenseDeptID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                        ON C.FDEPTID = E.FRELATION
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                   AND  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FExpenseOrgId = {fTOrg.OrgId}
                   AND  ISNULL(A.FBXBillNo,'') = '' --未下推费用报销单
                   AND  B.FExpID NOT IN (111685,111686) --非化料成本，辅料成本
                   AND  A.FBillNo NOT LIKE '%-%'
                 GROUP  BY A.FExpenseOrgId,C.F_ora_Assistant,C.FDEPTID,B.FExpID,E.FCostCenterID --专用车间,费用项目
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.OrgId = item["FOrgID"].ToString();
                cost.CostCenterId = item["FCostCenterID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }
            #endregion

            #region 4.获取费用报销单的金额
            sql = $@"
                SELECT  A.FExpenseOrgId FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,G.FExpID
                       ,ISNULL(E.FCostCenterID,0) FCostCenterID
                       ,SUM(G.FExpenseAmount)FAmount
                       ,C.FDEPTID
                  FROM  t_ER_ExpenseJt A WITH(NOLOCK)
		                INNER JOIN t_ER_ExpenseJtEntry B WITH(NOLOCK)
		                ON A.FID = B.FID
                        INNER JOIN T_ER_EXPENSEREIMBENTRY_LK F WITH(NOLOCK)
						ON F.FSID = B.FEntryID AND F.FSBILLID = B.FID
						INNER JOIN T_ER_EXPENSEREIMBENTRY G WITH(NOLOCK)
						ON G.FENTRYID = F.FENTRYID
						INNER JOIN T_ER_EXPENSEREIMB H WITH(NOLOCK)
						ON H.FID = G.FID 
		                INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                ON H.FExpenseDeptID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                        ON C.FDEPTID = E.FRELATION
                 WHERE  A.FDOCUMENTSTATUS = 'C'
                   AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                   AND  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FExpenseOrgId = {fTOrg.OrgId}
                   AND  ISNULL(A.FBXBillNo,'') <> '' --已下推费用报销单
                   AND  B.FExpID NOT IN (111685,111686) --非化料成本，辅料成本
                 GROUP  BY A.FExpenseOrgId,C.F_ora_Assistant,C.FDEPTID,G.FExpID,E.FCostCenterID --专用车间,费用项目
                ";
            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.OrgId = item["FOrgID"].ToString();
                cost.CostCenterId = item["FCostCenterID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.ExpID = item["FExpID"].ToString();
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
                ).FirstOrDefault();

                if (cost == null)
                {
                    costList.Add(new Cost()
                    {
                        OrgId = deptCost.OrgId,
                        CostCenterId = deptCost.CostCenterId,
                        IsZY = deptCost.IsZY,
                        ExpID = deptCost.ExpID,
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

        private void GetNum(FTOrg fTOrg)
        {

            custNumList.Clear();

            //1.如果组织是伊莱亚，数量从销售订单中获取
            //2.如果组织不是伊莱亚，数量从分摊录入中获取

            string sql = "";
            DynamicObjectCollection data = null;

            #region 1.从销售订单获取数量
            if (fTOrg.OrgId.Contains("100119"))
            {
                sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,SUM(FWeight)FWeight,SUM(FQTY)FQTY
				  FROM  (
                    SELECT  A.FSaleOrgId FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FNumber ELSE '0' END FZYDept
                           ,A.FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                           ,B.F_ora_Qty FWeight
                           ,B.F_ora_Qty1 FQty
                      FROM  T_SAL_ORDER A WITH(NOLOCK)
		                    INNER JOIN T_SAL_ORDEREntry B WITH(NOLOCK)
		                    ON A.FID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON A.FSALEDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                       AND  A.FDate >= '{fTOrg.BeginTime}'
                       AND  A.FDate < '{fTOrg.EndTime}'
                       AND  A.FSaleOrgId = {fTOrg.OrgId}
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId
                ";
                data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                foreach (var item in data)
                {
                    CustNum custNum = new CustNum();
                    custNum.OrgId = item["FOrgID"].ToString();
                    custNum.CostCenterId = item["FCostCenterId"].ToString();
                    custNum.CustID = item["FCustID"].ToString();
                    //custNum.MaterialID = item["FMaterialID"].ToString();
                    custNum.IsZY = item["FZYDept"].ToString();
                    custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                    custNum.Qty = Convert.ToDecimal(item["FQty"]);
                    custNumList.Add(custNum);
                }
            }
            #endregion

            #region 1.从成本分摊录入获取数量
            if (!fTOrg.OrgId.Contains("100119"))
            {
                sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,SUM(FWeight)FWeight,SUM(FQTY)FQTY
				  FROM  (
                    SELECT  A.FOrgID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FNumber ELSE '0' END FZYDept
                           ,B.FCustomerId FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                           ,B.FWEIGHT FWeight
                           ,B.FQTY FQty
                      FROM  YJ_T_CostShareImport A WITH(NOLOCK)
		                    INNER JOIN YJ_T_CostShareImportEntry B WITH(NOLOCK)
		                    ON A.FID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON A.FDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                       AND  A.FYear = '{fTOrg.Year}'
                       AND  A.FPeriod = '{fTOrg.Period}'
                       AND  A.FOrgID = {fTOrg.OrgId}
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId
                ";
                data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                foreach (var item in data)
                {
                    CustNum custNum = new CustNum();
                    custNum.OrgId = item["FOrgID"].ToString();
                    custNum.CostCenterId = item["FCostCenterId"].ToString();
                    custNum.CustID = item["FCustID"].ToString();
                    //custNum.MaterialID = item["FMaterialID"].ToString();
                    custNum.IsZY = item["FZYDept"].ToString();
                    custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                    custNum.Qty = Convert.ToDecimal(item["FQty"]);
                    custNumList.Add(custNum);
                }
            }

            #endregion

            if (custNumList.Count <= 0)
            {
                throw new Exception("未获取到对应期间的总重量或件数!");
            }
        }

        private void FTDetailCost(FTOrg fTOrg)
        {
            detailCostList.Clear();

            foreach (var cost in costList.Where(x => x.OrgId == fTOrg.OrgId))
            {
                List<CustNum> custNums = custNumList.Where(
                    x => x.OrgId == cost.OrgId && x.CostCenterId == cost.CostCenterId && x.IsZY == cost.IsZY).ToList();
                if (custNums.Count <= 0)
                {
                    continue;
                }

                //人工费按照件数分配
                //非人工费按照吨数分配
                decimal sumQty = cost.ExpID == "111693" ? custNums.Sum(x => x.Qty): custNums.Sum(x => x.Weight);
                decimal sharyAmount = 0;
                decimal sumSharyAmount = 0;

                int count = 0;

                foreach (var custNum in custNums)
                {
                    if (count != custNums.Count - 1)
                    {
                        sharyAmount = Math.Round(cost.Amount * ((cost.ExpID == "111693" ? custNum.Qty : custNum.Weight) / sumQty) ,2);
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
                        //MaterialID = custNum.MaterialID,
                        ExpID = cost.ExpID,
                        Amount = sharyAmount
                    };

                    sumSharyAmount = sumSharyAmount + sharyAmount;

                    detailCostList.Add(detailCost);

                    count++;
                }
                
            }
        }

        void CreateCostShare(FTOrg fTOrg,string billType = "CBFT1")
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
        private void FillBillPropertysByCostShare(IBillView billView, FTOrg fTOrg,string billType)
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

            //分摊明细单据体
            billView.Model.BatchCreateNewEntryRow("FEntity", detailCostList.Count);
            foreach (var detailCost in detailCostList.OrderByDescending(x=>x.CustID))
            {
                dynamicFormView.SetItemValueByID("FCustomerId", detailCost.CustID, index);
                dynamicFormView.SetItemValueByID("FCostCenter", detailCost.CostCenterId, index);
                dynamicFormView.SetItemValueByID("FExpense", detailCost.ExpID, index);
                dynamicFormView.UpdateValue("FAmount", index, detailCost.Amount);

                index++;
            }

            //制造费用单据体
            index = 0;
            billView.Model.BatchCreateNewEntryRow("FSumAmountEntry", deptCostList.Count);
            foreach (var deptCost in deptCostList.OrderByDescending(x => x.DeptID))
            {
                dynamicFormView.SetItemValueByID("FDeptID", deptCost.DeptID, index);
                dynamicFormView.SetItemValueByID("FExpenseID", deptCost.ExpID, index);
                dynamicFormView.UpdateValue("FExpAmount", index, deptCost.Amount);

                index++;
            }
        }
        #endregion

        #region 差额分摊
        private void DifFT(FTOrg fTOrg)
        {
            string billType = "CBFT2";

            //获取总差额
            GetDifShareData(fTOrg);
            //获取数量
            GetDifNum(fTOrg);
            //总成本分摊到客户产品
            FTDetailCost(fTOrg);
            //创建成本分摊单
            CreateCostShare(fTOrg,billType);
        }

        private void GetDifShareData(FTOrg fTOrg)
        {
            deptCostList.Clear();
            costList.Clear();
            DynamicObjectCollection data = null;
            string sql = $@"
                SELECT  A.FOrgID FOrgID
                       ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FDEPTID ELSE '0' END FZYDept
                       ,B.FExpID
                       ,ISNULL(E.FCostCenterID,0) FCostCenterID
                       ,SUM(B.FDifAmount) FAmount
                       ,C.FDEPTID
                  FROM  t_ER_ExpenseReimb A
                        INNER JOIN t_ER_ExpenseReimbEntry B
                        ON A.FID = B.FID
                        INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
	                    ON A.FExpenseDeptID = C.FDEPTID
		                INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                ON C.FDeptProperty = D.FENTRYID 
                        LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                        ON C.FDEPTID = E.FRELATION
                 WHERE  A.FDate >= '{fTOrg.BeginTime}'
                   AND  A.FDate < '{fTOrg.EndTime}'
                   AND  A.FDocumentStatus = 'C'
                   AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                   AND  A.FOrgID = {fTOrg.OrgId}
                 GROUP  BY A.FOrgID,C.F_ora_Assistant,C.FDEPTID,B.FExpID,E.FCostCenterID --专用车间,费用项目
                ";

            data = DBUtils.ExecuteDynamicObject(this.Context, sql);

            foreach (var item in data)
            {
                Cost cost = new Cost();
                cost.OrgId = item["FOrgID"].ToString();
                cost.CostCenterId = item["FCostCenterID"].ToString();
                cost.IsZY = item["FZYDept"].ToString();
                cost.DeptID = item["FDEPTID"].ToString();
                cost.ExpID = item["FExpID"].ToString();
                cost.Amount = Convert.ToDecimal(item["FAmount"]);
                deptCostList.Add(cost);
            }

            foreach (var deptCost in deptCostList)
            {
                Cost cost = costList.Where(x => x.OrgId == deptCost.OrgId
                                             && x.CostCenterId == deptCost.CostCenterId
                                             && x.IsZY == deptCost.IsZY
                                             && x.ExpID == deptCost.ExpID
                ).FirstOrDefault();

                if (cost == null)
                {
                    costList.Add(new Cost()
                    {
                        OrgId = deptCost.OrgId,
                        CostCenterId = deptCost.CostCenterId,
                        IsZY = deptCost.IsZY,
                        ExpID = deptCost.ExpID,
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


        private void GetDifNum(FTOrg fTOrg)
        {

            custNumList.Clear();

            //1.如果组织是伊莱亚，数量从销售订单中获取
            //2.如果组织不是伊莱亚，数量从分摊录入中获取

            string sql = "";
            DynamicObjectCollection data = null;

            #region 1.从销售订单获取数量
            if (fTOrg.OrgId.Contains("100119"))
            {
                sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,SUM(FWeight)FWeight,SUM(FQTY)FQTY
				  FROM  (
                    SELECT  A.FSaleOrgId FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FNumber ELSE '0' END FZYDept
                           ,A.FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                           ,B.F_ora_Qty FWeight
                           ,B.F_ora_Qty1 FQty
                      FROM  T_SAL_ORDER A WITH(NOLOCK)
		                    INNER JOIN T_SAL_ORDEREntry B WITH(NOLOCK)
		                    ON A.FID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON A.FSALEDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                       AND  A.FDate >= DATEADD(M,-1,'{fTOrg.BeginTime}')
                       AND  A.FDate < DATEADD(M,-1,'{fTOrg.EndTime}')
                       AND  A.FSaleOrgId = {fTOrg.OrgId}
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId
                ";
                data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                foreach (var item in data)
                {
                    CustNum custNum = new CustNum();
                    custNum.OrgId = item["FOrgID"].ToString();
                    custNum.CostCenterId = item["FCostCenterId"].ToString();
                    custNum.CustID = item["FCustID"].ToString();
                    //custNum.MaterialID = item["FMaterialID"].ToString();
                    custNum.IsZY = item["FZYDept"].ToString();
                    custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                    custNum.Qty = Convert.ToDecimal(item["FQty"]);
                    custNumList.Add(custNum);
                }
            }
            #endregion

            #region 1.从成本分摊录入获取数量
            if (!fTOrg.OrgId.Contains("100119"))
            {
                sql = $@"
				SELECT FOrgID,FZYDept,FCustID,FCostCenterId,SUM(FWeight)FWeight,SUM(FQTY)FQTY
				  FROM  (
                    SELECT  A.FOrgID FOrgID
                           ,CASE WHEN ISNULL(C.F_ora_Assistant,'') = '647588a1d3effb' THEN C.FNumber ELSE '0' END FZYDept
                           ,B.FCustomerId FCustID
                           ,B.FMaterialID
                           ,ISNULL(E.FCostCenterId,'')FCostCenterId
                           ,B.FWEIGHT FWeight
                           ,B.FQTY FQty
                      FROM  YJ_T_CostShareImport A WITH(NOLOCK)
		                    INNER JOIN YJ_T_CostShareImportEntry B WITH(NOLOCK)
		                    ON A.FID = B.FID
		                    INNER JOIN T_BD_DEPARTMENT C WITH(NOLOCK) --部门
		                    ON A.FDEPTID = C.FDEPTID
		                    INNER JOIN T_BAS_ASSISTANTDATAENTRY D WITH(NOLOCK) --部门属性
		                    ON C.FDeptProperty = D.FENTRYID 
                            LEFT JOIN T_CB_COSTCENTER E WITH(NOLOCK) --成本中心
                            ON C.FDEPTID = E.FRELATION
                     WHERE  A.FDOCUMENTSTATUS = 'C'
                       --AND  D.FNUMBER = 'DP01_SYS' --生产部门 
                       AND  A.FDate >= DATEADD(M,-1,'{fTOrg.BeginTime}')
                       AND  A.FDate < DATEADD(M,-1,'{fTOrg.EndTime}')
                       AND  A.FOrgID = {fTOrg.OrgId}
                    ) A
                 GROUP  BY FOrgID,FZYDept,FCustID,FCostCenterId
                ";
                data = DBUtils.ExecuteDynamicObject(this.Context, sql);

                foreach (var item in data)
                {
                    CustNum custNum = new CustNum();
                    custNum.OrgId = item["FOrgID"].ToString();
                    custNum.CostCenterId = item["FCostCenterId"].ToString();
                    custNum.CustID = item["FCustID"].ToString();
                    //custNum.MaterialID = item["FMaterialID"].ToString();
                    custNum.IsZY = item["FZYDept"].ToString();
                    custNum.Weight = Convert.ToDecimal(item["FWeight"]);
                    custNum.Qty = Convert.ToDecimal(item["FQty"]);
                    custNumList.Add(custNum);
                }
            }

            #endregion

            if (custNumList.Count <= 0)
            {
                throw new Exception("未获取到对应期间的总重量或件数!");
            }
        }
        #endregion

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

                fTOrg.BeginTime = $@"{fTOrg.Year}-{fTOrg.Period}-01";
                fTOrg.EndTime = Convert.ToDateTime(fTOrg.BeginTime).AddMonths(1).ToString("yyyy-MM-dd");

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
        /// 保存物料，并显示保存结果
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
        /// <summary>
        /// 加载指定的单据进行修改
        /// </summary>
        /// <param name="billView"></param>
        /// <param name="pkValue"></param>
        private void ModifyBill(IBillView billView, string pkValue)
        {
            billView.OpenParameter.Status = OperationStatus.EDIT;
            billView.OpenParameter.CreateFrom = CreateFrom.Default;
            billView.OpenParameter.PkValue = pkValue;
            billView.OpenParameter.DefaultBillTypeId = string.Empty;
            ((IDynamicFormViewService)billView).LoadData();
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
        /// 费用项目
        /// </summary>
        public string ExpID { get; set; }

        /// <summary>
        /// 金额
        /// </summary>
        public decimal Amount = 0;
    }

    //每个客户每个产品的数量
    public class CustNum
    {
        /// <summary>
        /// 组织
        /// </summary>
        public string OrgId { get; set; }

        /// <summary>
        /// 成本中心
        /// </summary>
        public string CostCenterId { get; set; }

        public string IsZY { get; set; }

        public string CustID { get; set; }

        public string MaterialID { get; set; }


        public decimal Weight { get; set; }

        public decimal Qty { get; set; }
    }

    //每个组织每个费用项目每个成本中心每个客户每个产品的成本
    public class DetailCost
    {
        public string OrgId { get; set; }

        public string CostCenterId { get; set; }

        public string ExpID { get; set; }

        /// <summary>
        /// 专用车间
        /// </summary>
        public string IsZY { get; set; }

        public string CustID { get; set; }

        public string MaterialID { get; set; }

        public decimal Amount { get; set; }
    }

    public class ShareResult
    {
        public FTOrg Org { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }
    }
}
