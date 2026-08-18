using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AgentSyncConsole.Models
{
    /// <summary>
    /// One entry from parsed.response.data.agents / parsed.hotelogix.response.data.agents.
    /// Fields are read defensively (matching the original `agentData.field || ""` pattern),
    /// so numeric-ish raw tokens (mobileNo/phoneNo/zip) are kept as JToken and converted with
    /// JsonHelper.ParseIntOrZero at the point they were parseInt()'d in the original code.
    /// </summary>
    public class AgentRecord
    {
        public string Id { get; set; } = "";
        public string Organization { get; set; } = "";
        public string FName { get; set; } = "";
        public string LName { get; set; } = "";
        public string Email { get; set; } = "";
        public JToken MobileNoRaw { get; set; }
        public JToken PhoneNoRaw { get; set; }
        public string Code { get; set; } = "";
        public string HotelId { get; set; } = "";

        public List<GstinDetail> GstinDetails { get; set; } = new List<GstinDetail>();
        public AgentAddresses Addresses { get; set; } = new AgentAddresses();
    }

    public class CorporatesRecord
    {
        public string Id { get; set; } = "";
        public string Organization { get; set; } = "";
        public string FName { get; set; } = "";
        public string LName { get; set; } = "";
        public string Email { get; set; } = "";
        public JToken MobileNoRaw { get; set; }
        public JToken PhoneNoRaw { get; set; }
        public string Code { get; set; } = "";
        public string HotelId { get; set; } = "";

        public List<GstinDetail> GstinDetails { get; set; } = new List<GstinDetail>();
        public AgentAddresses Addresses { get; set; } = new AgentAddresses();
    }

    /// <summary>One entry of agentData.gstinDetails[]</summary>
    public class GstinDetail
    {
        public string Gstin { get; set; } = "";
        public string Name { get; set; } = "";

        // null == field was absent/not boolean in the source JSON (mirrors JS `undefined`)
        public bool? IsDefault { get; set; }
    }

    /// <summary>agentData.addresses.home / agentData.addresses.work (or "worK")</summary>
    public class AddressInfo
    {
        public string Country { get; set; } = "";
        public string State { get; set; } = "";
        public string City { get; set; } = "";

        // Kept raw - original does parseInt(home.zip, 10) at mapping time.
        public JToken Zip { get; set; }
    }

    public class AgentAddresses
    {
        public AddressInfo Home { get; set; } = new AddressInfo();
        public AddressInfo Work { get; set; } = new AddressInfo();
    }
}
