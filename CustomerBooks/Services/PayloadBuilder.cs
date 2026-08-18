using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using AgentSyncConsole.CustomerBooks.Models;

namespace AgentSyncConsole.CustomerBooks.Services
{
    /// <summary>
    /// Builds the Zoho Books contact / tax-info JSON payloads. Field mapping
    /// is kept as-is per the original CustomerBooksSync.Api implementation.
    /// </summary>
    public static class PayloadBuilder
    {
        private static readonly HashSet<string> BusinessSubTypeValues =
            new(StringComparer.OrdinalIgnoreCase) { "corporate", "agent", "company", "business" };

        private static readonly HashSet<string> IndividualSubTypeValues =
            new(StringComparer.OrdinalIgnoreCase) { "guest", "individual", "personal" };

        public static JsonObject BuildContactPayload(CustomerRecord customer, GstMasterRecord? defaultGst, string codeCustomFieldId, string HotelIDCustomField)
        {
            var contactName = ValidationHelpers.IsNonEmpty(customer.Company_Name)
                ? customer.Company_Name
                : string.Join(' ', new[] { customer.First_Name, customer.Last_Name }.Where(ValidationHelpers.IsNonEmpty));

            var gstNo = defaultGst is not null && ValidationHelpers.IsNonEmpty(defaultGst.GST_No)
                ? defaultGst.GST_No
                : (ValidationHelpers.IsNonEmpty(customer.GST_NO) ? customer.GST_NO : null);

            var placeOfContact = defaultGst is not null && ValidationHelpers.IsNonEmpty(defaultGst.Place_Of_Supply)
                ? defaultGst.Place_Of_Supply
                : customer.Place_of_Supply;

            var legalName = defaultGst is not null && ValidationHelpers.IsNonEmpty(defaultGst.Name)
                ? defaultGst.Name
                : null;

            var payload = new JsonObject
            {
                ["contact_name"] = contactName,
                ["company_name"] = customer.Company_Name,
                ["contact_type"] = "customer",
                ["customer_sub_type"] = MapCustomerSubType(customer.Customer_Sub_Type),
                ["contact_persons"] = BuildContactPersons(customer),
                ["billing_address"] = BuildAddress(customer, "Billing"),
                ["shipping_address"] = BuildAddress(customer, "Shipping"),
                ["gst_treatment"] = BuildGstTreatment(customer),
                ["gst_no"] = gstNo,
                ["pan_no"] = ValidationHelpers.IsNonEmpty(customer.Pan_No) ? customer.Pan_No : null,
                ["currency_code"] = customer.Currency,
                ["place_of_contact"] = placeOfContact,
                ["legal_name"] = legalName,
                ["trader_name"] = legalName,
                ["is_taxable"] = customer.Tax_Preference == true ? (bool?)true : null,
                ["tax_preference"] = customer.Tax_Preference,
                ["email"] = ValidationHelpers.IsValidEmail(customer.Email) ? customer.Email : null,
                ["phone"] = ValidationHelpers.IsValidPhone(customer.Phone) ? customer.Phone : null,
                ["mobile"] = ValidationHelpers.IsValidPhone(customer.Mobile) ? customer.Mobile : null,
                ["custom_fields"] = BuildCustomFields(customer, codeCustomFieldId, HotelIDCustomField)
            };

            return RemoveEmptyValues(payload);
        }

        public static JsonObject BuildTaxInfoPayload(GstMasterRecord gstRow)
        {
            var payload = new JsonObject
            {
                ["tax_registration_no"] = gstRow.GST_No,
                ["place_of_supply"] = gstRow.Place_Of_Supply,
                ["legal_name"] = gstRow.Name,
                ["trader_name"] = gstRow.Name
            };

            return RemoveEmptyValues(payload);
        }

        private static JsonObject BuildAddress(CustomerRecord customer, string prefix)
        {
            // Billing_Address / Shipping_Address do not exist as single columns —
            // only *_City, *_State, *_Pincode, *_Country, *_Phone do.
            string? city, state, zip, country, phone;

            if (prefix == "Billing")
            {
                city = customer.Billing_City;
                state = customer.Billing_State;
                zip = customer.Billing_Pincode;
                country = customer.Billing_Country;
                phone = customer.Billing_Phone;
            }
            else
            {
                city = customer.Shipping_City;
                state = customer.Shipping_State;
                zip = customer.Shipping_Pincode;
                country = customer.Shipping_Country;
                phone = customer.Shipping_Phone;
            }

            var address = new JsonObject
            {
                ["city"] = city,
                ["state"] = state,
                ["zip"] = zip,
                ["country"] = country,
                ["phone"] = ValidationHelpers.IsValidPhone(phone) ? phone : null
            };

            return RemoveEmptyValues(address);
        }

        private static JsonArray BuildContactPersons(CustomerRecord customer)
        {
            if (!ValidationHelpers.IsNonEmpty(customer.First_Name) && !ValidationHelpers.IsNonEmpty(customer.Last_Name))
            {
                return new JsonArray();
            }

            var person = new JsonObject
            {
                ["first_name"] = customer.First_Name,
                ["last_name"] = customer.Last_Name,
                ["email"] = ValidationHelpers.IsValidEmail(customer.Email) ? customer.Email : null,
                ["phone"] = ValidationHelpers.IsValidPhone(customer.Phone) ? customer.Phone : null,
                ["mobile"] = ValidationHelpers.IsValidPhone(customer.Mobile) ? customer.Mobile : null,
                ["is_primary_contact"] = true
            };

            var cleaned = RemoveEmptyValues(person);
            return cleaned.Count > 0 ? new JsonArray(cleaned) : new JsonArray();
        }

        // CRM sends free-text Customer_Sub_Type values; Zoho Books only accepts
        // "business" or "individual". Never forward the raw CRM value.
        private static string? MapCustomerSubType(string? rawSubType)
        {
            if (!ValidationHelpers.IsNonEmpty(rawSubType))
            {
                return null;
            }

            var normalized = rawSubType!.Trim();

            if (BusinessSubTypeValues.Contains(normalized))
            {
                return "business";
            }

            if (IndividualSubTypeValues.Contains(normalized))
            {
                return "individual";
            }

            return null; // unrecognized CRM value — not sent to Books.
        }

        private static string BuildGstTreatment(CustomerRecord customer)
        {
            // Never overwrite a real CRM value — only fall back when it's actually missing.
            return ValidationHelpers.IsNonEmpty(customer.GST_Treatment)
                ? customer.GST_Treatment!.Trim()
                : "business_none";
        }

        private static JsonArray BuildCustomFields(CustomerRecord customer, string codeCustomFieldId, string HotelIDCustomField)
        {
             var customFields = new JsonArray();

    if (ValidationHelpers.IsNonEmpty(customer.Code))
    {
        customFields.Add(new JsonObject
        {
            ["customfield_id"] = codeCustomFieldId,
            ["value"] = customer.Code
        });
    }

    if (ValidationHelpers.IsNonEmpty(customer.hotelID))
    {
        customFields.Add(new JsonObject
        {
            ["customfield_id"] = HotelIDCustomField,
            ["value"] = customer.hotelID
        });
    }

    return customFields;
        }

        // Recursively strips null / empty-string / empty-array / empty-object
        // values. Booleans (including false) and numbers (including 0) are
        // preserved.
        private static JsonObject RemoveEmptyValues(JsonObject input)
        {
            var result = new JsonObject();

            foreach (var (key, value) in input)
            {
                var cleaned = CleanValue(value);
                if (!IsEmpty(cleaned))
                {
                    result[key] = cleaned;
                }
            }

            return result;
        }

        private static JsonNode? CleanValue(JsonNode? value)
        {
            switch (value)
            {
                case JsonObject obj:
                    return RemoveEmptyValues(obj);

                case JsonArray arr:
                    var cleanedArray = new JsonArray();
                    foreach (var item in arr)
                    {
                        var cleanedItem = CleanValue(item);
                        if (!IsEmpty(cleanedItem))
                        {
                            cleanedArray.Add(cleanedItem);
                        }
                    }
                    return cleanedArray;

                default:
                    return value?.DeepClone();
            }
        }

        private static bool IsEmpty(JsonNode? value)
        {
            if (value is null)
            {
                return true;
            }

            if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var str))
            {
                return string.IsNullOrWhiteSpace(str);
            }

            if (value is JsonArray arr)
            {
                return arr.Count == 0;
            }

            if (value is JsonObject obj)
            {
                return obj.Count == 0;
            }

            // Numbers and booleans (including 0 / false) are never empty.
            return false;
        }
    }
}
