const DEFAULTS = Object.freeze({
  LEGACY_CLIENT_ID: "00000000000000000000000000000000",
  LEGACY_APL_VERSION: "1.2.35",
});

function doGet(e) {
  const version = latestVersion_();
  if (version === null) {
    return jsonResponse_({ error: "version_sheet_not_found" });
  }

  const settings = settings_();
  let telemetryResult;
  try {
    telemetryResult = handleSession_(e, settings);
  } catch (_) {
    // Telemetry must never break the version endpoint.
    telemetryResult = failure_("internal_error");
  }

  const output = {
    version: version,
    website_url: settings.websiteUrl,
    shops_url: settings.shopsUrl,
    donate_url: settings.donateUrl,
    community_url: settings.communityUrl,
    telemetry_ok: telemetryResult.ok,
  };
  if (!telemetryResult.ok) {
    output.telemetry_error = telemetryResult.error;
  }
  return jsonResponse_(output);
}

function doPost(e) {
  try {
    return jsonResponse_(handlePost_(e, settings_()));
  } catch (_) {
    return jsonResponse_(failure_("internal_error"));
  }
}

function handleSession_(e, settings) {
  const input = sessionInputFromGet_(e);
  return sendGa4_(
    input === null ? legacyGa4Payload_(settings) : ga4Payload_(input),
    settings
  );
}

function handlePost_(e, settings) {
  const input = jsonPostInput_(e);
  if (input === null) {
    return failure_("invalid_json");
  }
  return isErrorReport_(input)
    ? sendErrorReport_(input, settings)
    : sendGa4_(ga4Payload_(input), settings);
}

function latestVersion_() {
  const sheet = SpreadsheetApp
    .getActiveSpreadsheet()
    .getSheetByName("Version");
  return sheet === null
    ? null
    : String(sheet.getRange("A1").getValue() || "");
}

function settings_() {
  const properties = PropertiesService.getScriptProperties();
  return {
    measurementId: properties.getProperty("GA4_MEASUREMENT_ID"),
    apiSecret: properties.getProperty("GA4_API_SECRET"),
    errorWebhookUrl: properties.getProperty("ERROR_WEBHOOK_URL"),
    websiteUrl: properties.getProperty("WEBSITE_URL") || "",
    shopsUrl: properties.getProperty("SHOPS_URL") || "",
    donateUrl: properties.getProperty("DONATE_URL") || "",
    communityUrl: properties.getProperty("COMMUNITY_URL") || "",
    legacyClientId: properties.getProperty("GA4_LEGACY_CLIENT_ID")
      || DEFAULTS.LEGACY_CLIENT_ID,
  };
}

function sessionInputFromGet_(e) {
  const input = Object.assign({}, e && isObject_(e.parameter) ? e.parameter : {});
  if (!input.client_id) {
    return null;
  }
  input.schema_version = Number(input.schema_version);
  if ("batch_mode" in input) input.batch_mode = Number(input.batch_mode);
  if ("session_id" in input) input.session_id = Number(input.session_id);
  if ("engagement_time_msec" in input) {
    input.engagement_time_msec = Number(input.engagement_time_msec);
  }
  return input;
}

function jsonPostInput_(e) {
  try {
    return JSON.parse(e.postData.contents);
  } catch (_) {
    return null;
  }
}

function isErrorReport_(input) {
  return isObject_(input) && input.request_type === "error_report";
}

function ga4Payload_(input) {
  const params = Object.assign({}, input);
  delete params.client_id;
  delete params.event_name;
  params.engagement_time_msec = input.engagement_time_msec || 1;
  return eventPayload_(input.client_id, input.event_name, params);
}

function legacyGa4Payload_(settings) {
  return eventPayload_(settings.legacyClientId, "apl_editor_session_started", {
    apl_version: DEFAULTS.LEGACY_APL_VERSION,
    engagement_time_msec: 1,
  });
}

function eventPayload_(clientId, eventName, params) {
  return {
    client_id: clientId,
    consent: {
      ad_user_data: "DENIED",
      ad_personalization: "DENIED",
    },
    events: [{
      name: eventName,
      params: params,
    }],
  };
}

function sendGa4_(payload, settings) {
  if (!settings.measurementId || !settings.apiSecret) {
    return failure_("not_configured");
  }

  const response = UrlFetchApp.fetch(ga4Url_(settings), {
    method: "post",
    contentType: "application/json",
    payload: JSON.stringify(payload),
    muteHttpExceptions: true,
  });
  if (!isSuccessfulResponse_(response)) {
    return failure_("upstream_error");
  }
  return success_();
}

function ga4Url_(settings) {
  return "https://www.google-analytics.com/mp/collect"
    + "?measurement_id=" + encodeURIComponent(settings.measurementId)
    + "&api_secret=" + encodeURIComponent(settings.apiSecret);
}


function sendErrorReport_(report, settings) {
  const url = webhookUrl_(settings);
  if (url === "") {
    return failure_("webhook_not_configured");
  }

  const response = UrlFetchApp.fetch(
    url,
    webhookErrorRequest_(sanitizedErrorReport_(report))
  );
  return isSuccessfulResponse_(response)
    ? success_()
    : failure_("webhook_upstream_error");
}

function webhookUrl_(settings) {
  return String(settings.errorWebhookUrl || "").trim();
}

function webhookErrorRequest_(report) {
  const fileName = "apl-error-" + report.report_id + ".json";
  const message = {
    content: [
      "AvatarPoseLibrary error report",
      "APL: " + report.apl_version,
      "Stage: " + report.build_stage,
      "Exception: " + report.exception_type,
    ].join("\n"),
    allowed_mentions: { parse: [] },
    attachments: [{ id: 0, filename: fileName }],
  };
  return {
    method: "post",
    payload: {
      payload_json: JSON.stringify(message),
      "files[0]": Utilities.newBlob(
        JSON.stringify(report, null, 2),
        "application/json",
        fileName
      ),
    },
    muteHttpExceptions: true,
  };
}

function sanitizedErrorReport_(report) {
  const safe = JSON.parse(JSON.stringify(report));
  delete safe.request_type;
  return safe;
}

function isSuccessfulResponse_(response) {
  const code = response.getResponseCode();
  return code >= 200 && code < 300;
}

function isObject_(value) {
  return value !== null && !Array.isArray(value) && typeof value === "object";
}

function success_() {
  return { ok: true };
}

function failure_(error) {
  return { ok: false, error: error };
}

function jsonResponse_(value) {
  return ContentService
    .createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}
