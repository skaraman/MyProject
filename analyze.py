import csv

rows = list(csv.DictReader(open(r'D:\localDev\Unity\Esperanza\MyProject\ProfilerCaptures\latest.csv')))
rows.sort(key=lambda x: int(x['gc_alloc_bytes']), reverse=True)

print("TOP ALLOCATIONS:")
for r in rows[:15]:
    print(f"{r['gc_alloc_bytes']} bytes - {r['path']}")
