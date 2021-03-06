using LibAtem.Commands;
using LibAtem.Common;
using LibAtem.Serialization;

namespace LibAtem.MacroOperations.SuperSource
{
    public abstract class SuperSourceBoxMacroOpBase : MacroOpBase
    {
        [CommandId]
        [Serialize(4), Enum8]
        [MacroField("SuperSourceBoxIndex", "boxIndex")]
        public SuperSourceBoxId BoxIndex { get; set; }
    }
}