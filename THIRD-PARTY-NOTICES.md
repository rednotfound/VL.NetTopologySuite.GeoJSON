# Third-party notices

VL.NetTopologySuite.GeoJSON is MIT-licensed (see LICENSE). Its package ships two sample files in
`help/Assets/`:

| file | source | licence | what it asks of you |
|---|---|---|---|
| `cities.geojson` | hand-written for this package: five cities and a rough box around Kanto | MIT, same as the package | nothing |
| `ne_10m_populated_places_simple.geojson` | [Natural Earth](https://www.naturalearthdata.com) 1:10m Populated Places (simple) | public domain | nothing; Natural Earth asks for a credit such as "Made with Natural Earth" where convenient |

Natural Earth puts its data in the public domain, for any use including modification and
redistribution: https://www.naturalearthdata.com/about/terms-of-use/

The libraries this package uses are not inside it: NuGet installs them, each under its own licence
- NetTopologySuite, NetTopologySuite.Features and NetTopologySuite.IO.GeoJSON4STJ (BSD-3-Clause),
and VL.NetTopologySuite (MIT).
