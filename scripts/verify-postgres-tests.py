"""Fail CI if PostgreSQL tests were omitted, skipped, or failed in the TRX report."""
import sys
import xml.etree.ElementTree as ET

report = ET.parse(sys.argv[1])
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
required = {
    test.attrib["id"]: test.attrib["name"]
    for test in report.findall(".//t:TestDefinitions/t:UnitTest", ns)
    if "PostgreSqlStabilizationTests" in test.find("t:TestMethod", ns).attrib["className"]
}
results = {
    result.attrib["testId"]: result.attrib["outcome"]
    for result in report.findall(".//t:Results/t:UnitTestResult", ns)
}
if not required:
    sys.exit("No PostgreSQL integrity tests were discovered.")
failures = [f"{name}: {results.get(test_id, 'Missing')}"
            for test_id, name in required.items() if results.get(test_id) != "Passed"]
if failures:
    sys.exit("Required PostgreSQL tests did not pass:\n" + "\n".join(failures))
print(f"Verified {len(required)} PostgreSQL integrity tests passed.")
