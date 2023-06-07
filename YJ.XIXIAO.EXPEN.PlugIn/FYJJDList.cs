using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Util;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YJ.XIXIAO.EXPEN.PlugIn
{
    [Description("销售出库单-列表插件")]
    [HotUpdate]
    public class FYJJDList: AbstractListPlugIn
    {
        public override void AfterBarItemClick(AfterBarItemClickEventArgs e)
        {
            base.AfterBarItemClick(e);

            if (e.BarItemKey.ToUpper() == "TBEDITPDAERROR")
            {

                View.ShowMessage("更新成功");
            }
        }

        void CreateCBFT()
        {

        }

        /// <summary>
        /// 获取费用项目
        /// </summary>
        void GetExpenId()
        {

        }
    }
}
