# Windows NetFt: selecting submessage patterns

`submessage.messageid.token=Message` routes by the entire captured message. A definition with `messageid=Var1` is selected only when that value is exactly `Var1`. IDs are case-sensitive values, not labels. If no ID matches and there is no default definition, no submessage regex is tested. Increasing the number of definitions does not change this rule.

For `[DROP] Dropping received RCP messages from 10.10.10.10:3333.`, none of the supplied IDs (`Var1`, a full validation message, `N/A`, `NetftTraceMessage`, `StavovaZprava`) match. This is a routing configuration issue, not a failure to recognize the DROP regex.

Two supported approaches:

1. For regex-only classification, omit `submessage.messageid.token`, use one default definition without `submessage[0].messageid`, and add ordered patterns beneath it. The first successful pattern wins.
2. For ID routing, use a stable main token such as `WindowsEventId` or `Info`, with exact values in each definition. For this event those are `136` and `Netft Trace Message`. `NetftTraceMessage` does not equal `Netft Trace Message`. A known ID whose patterns fail does not fall back to the default.

The independent example [parser](../examples/windows-netft-pattern-routing.sdkrfilereader.properties) uses approach 1. Load it with the accompanying [log file](../examples/windows-netft-pattern-routing.log): the four events select patterns 0 (DROP), 1 (validation), 2 (N/A), and 3 (simple status). The duplicate validation alternatives from the supplied draft are consolidated. Address dots and the terminal DROP period are escaped; capture order matches event fields.

This example deliberately splits 15 tab-delimited fields instead of restricting each field's characters. It retains the supplied token names; `Message` is token[13] (group 14), while `Something` is token[12] (group 13), empty in this event. The hostname is anonymized and generic Windows event mappings replace the SystemDataArchiver-specific constants, since this log is from FailoverClustering. Review field formats and mappings for your device before deployment. Local previews do not execute ArcSight conversions.

The sample log file contains actual tabs. Text pasted through chat may instead contain pairs of EM SPACE (U+2003), which do not match `\t`. The parser file is a normal `.properties` file, without the chat formatting backslashes at the ends of every line.

In the log workbench, select a failed event to see the routing token, captured ID and configured IDs. When no route exists, the details explicitly state that no pattern was tested. **Edit submessage** still supports independent pattern previews, which do not imply that the connector selected that pattern.
