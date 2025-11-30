using System.Globalization;
using System.Xml.Linq;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using TollService.Application.Common.Interfaces;
using TollService.Domain;

namespace TollService.Application.TollPriceParser.NY;

/// <summary>
/// Парсит HTML/XML фрагмент с &lt;interchanges&gt; и создает/обновляет Toll'ы
/// по координатам, отфильтровывая только tolled="true".
/// Ожидает кусок вида:
/// &lt;interchanges&gt;
///   &lt;interchange id="..." latitude=".." longitude=".." exitid=".." description=".." tolled="true" /&gt;
///   ...
/// &lt;/interchanges&gt;
/// </summary>
public record ParseNewYorkInterchangesCommand(string HtmlFragment) : IRequest<ParseMassachusettsInterchangesResult>;

public record ParseMassachusettsInterchangesResult(
    int ProcessedInterchanges,
    int CreatedTolls,
    int UpdatedTolls,
    List<string> Errors);



public class ParseMassachusettsInterchangesCommandHandler(
    ITollDbContext context)
    : IRequestHandler<ParseNewYorkInterchangesCommand, ParseMassachusettsInterchangesResult>
{


    #region htmlCode

    public string htmlCode = "<interchanges> <interchange id=\"m00x\" latitude=\"40.90564\" longitude=\"-73.87817\" routeid=\"ML\" milepost=\"0.00\" type=\"Line\" exitid=\"\" description=\"New York City Line - Major Deegan Expressway (I-87)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m01x\" latitude=\"40.91210\" longitude=\"-73.87535\" routeid=\"ML\" milepost=\"0.48\" type=\"Interchange\" exitid=\"1\" description=\"Hall Place - McLean Avenue\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m02x\" latitude=\"40.92149\" longitude=\"-73.86309\" routeid=\"ML\" milepost=\"0.92\" type=\"Interchange\" exitid=\"2\" description=\"Yonkers Avenue - Raceway (No Southbound Exit)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m03x\" latitude=\"40.92510\" longitude=\"-73.85850\" routeid=\"ML\" milepost=\"1.77\" type=\"Interchange\" exitid=\"3\" description=\"Mile Square Road (No Southbound Exit)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m04x\" latitude=\"40.93086\" longitude=\"-73.85683\" routeid=\"ML\" milepost=\"2.18\" type=\"Interchange\" exitid=\"4\" description=\"Cross County Parkway - Mile Square Road\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m05x\" latitude=\"40.93833\" longitude=\"-73.85564\" routeid=\"ML\" milepost=\"2.70\" type=\"Interchange\" exitid=\"5\" description=\"White Plains - Central Park Avenue (NY Route 100)(No Southbound Exit)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m06x\" latitude=\"40.95496\" longitude=\"-73.86093\" routeid=\"ML\" milepost=\"4.00\" type=\"Interchange\" exitid=\"6\" description=\"(6 Northbound; 6E &amp; 6W Southbound) Yonkers - Bronxville - Tuckahoe Road\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m06a\" latitude=\"40.96670\" longitude=\"-73.86080\" routeid=\"ML\" milepost=\"5.14\" type=\"Interchange\" exitid=\"6A\" description=\"Stew Leonard Drive\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m07x\" latitude=\"41.00872\" longitude=\"-73.85099\" routeid=\"ML\" milepost=\"7.58\" type=\"Interchange\" exitid=\"7\" description=\"Ardsley - NY Route 9A (No Southbound Exit)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m07a\" latitude=\"41.04259\" longitude=\"-73.83608\" routeid=\"ML\" milepost=\"10.33\" type=\"Interchange\" exitid=\"7A\" description=\"Saw Mill River Parkway - Taconic State Parkway\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m08x\" latitude=\"41.05656\" longitude=\"-73.83533\" routeid=\"ML\" milepost=\"11.31\" type=\"Interchange\" exitid=\"8\" description=\"White Plains - Rye - Cross Westchester Expressway (I-287)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m08a\" latitude=\"41.05769\" longitude=\"-73.83732\" routeid=\"ML\" milepost=\"11.80\" type=\"Interchange\" exitid=\"8A\" description=\"Elmsford - NY Route 119 - Saw Mill River Parkway North (No Northbound Exit)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m09x\" latitude=\"41.06267\" longitude=\"-73.86195\" routeid=\"ML\" milepost=\"12.65\" type=\"Interchange\" exitid=\"9\" description=\"Tarrytown - Sleepy Hollow - US Route 9\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m10x\" latitude=\"41.08199\" longitude=\"-73.92317\" routeid=\"ML\" milepost=\"16.75\" type=\"Interchange\" exitid=\"10\" description=\"Nyack - South Nyack - US Route 9W (No Southbound Exit)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m11x\" latitude=\"41.09071\" longitude=\"-73.92879\" routeid=\"ML\" milepost=\"17.63\" type=\"Interchange\" exitid=\"11\" description=\"Nyack - South Nyack - US Route 9W\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m12x\" latitude=\"41.09957\" longitude=\"-73.95094\" routeid=\"ML\" milepost=\"18.76\" type=\"Interchange\" exitid=\"12\" description=\"West Nyack - NY Route 303 - Palisades Center Drive\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m13x\" latitude=\"41.10179\" longitude=\"-73.99245\" routeid=\"ML\" milepost=\"20.94\" type=\"Interchange\" exitid=\"13\" description=\"(13N &amp; 13S in Both Directions) New Jersey - Palisades Interstate Parkway - Bear Mountain State Park\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m14x\" latitude=\"41.10272\" longitude=\"-74.02769\" routeid=\"ML\" milepost=\"22.80\" type=\"Interchange\" exitid=\"14\" description=\"Spring Valley - Nanuet - NY Route 59\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m14a\" latitude=\"41.09768\" longitude=\"-74.03997\" routeid=\"ML\" milepost=\"23.53\" type=\"Interchange\" exitid=\"14A\" description=\"New Jersey – Garden State Parkway\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"GSP\" latitude=\"41.07341\" longitude=\"-74.05202\" routeid=\"ML\" milepost=\"1.48\" type=\"Exit\" exitid=\"GSP\" description=\"Schoolhouse Road - Chestnut Ridge (No Northbound Exit)\" route=\"Garden State Parkway Connector\" tolled=\"false\" /> <interchange id=\"m14b\" latitude=\"41.11629\" longitude=\"-74.11232\" routeid=\"ML\" milepost=\"27.62\" type=\"Interchange\" exitid=\"14B\" description=\"Airmont - Montebello - Airmont Road\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m15x\" latitude=\"41.12133\" longitude=\"-74.16283\" routeid=\"ML\" milepost=\"30.17\" type=\"Interchange\" exitid=\"15\" description=\"New Jersey - I-287 - NJ Route 17 South\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m15a\" latitude=\"41.13408\" longitude=\"-74.16819\" routeid=\"ML\" milepost=\"31.35\" type=\"Interchange\" exitid=\"15A\" description=\"Sloatsburg - Suffern - NY Routes 17 North &amp; 59\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m16x\" latitude=\"41.31065\" longitude=\"-74.12265\" routeid=\"ML\" milepost=\"45.20\" type=\"Interchange\" exitid=\"16\" description=\"Harriman - US Route 6 - NY Route 17\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m17x\" latitude=\"41.50947\" longitude=\"-74.07446\" routeid=\"ML\" milepost=\"60.10\" type=\"Interchange\" exitid=\"17\" description=\"Newburgh - Scranton- I-84 - NY Routes 17K &amp; 300\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m18x\" latitude=\"41.73551\" longitude=\"-74.06570\" routeid=\"ML\" milepost=\"76.01\" type=\"Interchange\" exitid=\"18\" description=\"New Paltz - Poughkeepsie - NY Route 299\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m19x\" latitude=\"41.94764\" longitude=\"-74.02749\" routeid=\"ML\" milepost=\"91.37\" type=\"Interchange\" exitid=\"19\" description=\"Kingston - NY Route 28 – Kingston-Rhinecliff Bridge\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m20x\" latitude=\"42.08306\" longitude=\"-73.97328\" routeid=\"ML\" milepost=\"101.25\" type=\"Interchange\" exitid=\"20\" description=\"Saugerties - Woodstock - NY Route 32\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m21x\" latitude=\"42.25013\" longitude=\"-73.88444\" routeid=\"ML\" milepost=\"113.89\" type=\"Interchange\" exitid=\"21\" description=\"Catskill - Cairo - NY Route 23\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m21b\" latitude=\"42.39365\" longitude=\"-73.82495\" routeid=\"ML\" milepost=\"124.53\" type=\"Interchange\" exitid=\"21B\" description=\"Coxsackie - Ravena - US Route 9W - NY Route 81\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m21a\" latitude=\"42.51557\" longitude=\"-73.78949\" routeid=\"ML\" milepost=\"133.60\" type=\"Interchange\" exitid=\"21A\" description=\"Boston - Massachusetts Turnpike - Berkshire Connector (I-90 East)\" route=\"I-87 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m22x\" latitude=\"42.53415\" longitude=\"-73.78411\" routeid=\"ML\" milepost=\"134.93\" type=\"Interchange\" exitid=\"22\" description=\"Selkirk - NY Routes 144 &amp; 396\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m23x\" latitude=\"42.63289\" longitude=\"-73.78142\" routeid=\"ML\" milepost=\"141.92\" type=\"Interchange\" exitid=\"23\" description=\"Albany (Downtown) - Troy - Rensselaer - I-787 - US Route 9W\" route=\"I-87 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m24x\" latitude=\"42.69778\" longitude=\"-73.84546\" routeid=\"ML\" milepost=\"148.15\" type=\"Interchange\" exitid=\"24\" description=\"Albany - Montreal - I-90 East - I-87 North\" route=\"I-87/I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m25x\" latitude=\"42.75400\" longitude=\"-73.93361\" routeid=\"ML\" milepost=\"153.83\" type=\"Interchange\" exitid=\"25\" description=\"Schenectady - I-890 - NY Routes 7 &amp; 146\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m25a\" latitude=\"42.79035\" longitude=\"-74.01592\" routeid=\"ML\" milepost=\"158.82\" type=\"Interchange\" exitid=\"25A\" description=\"Schenectady - Binghamton - I-88 - NY Route 7\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m26x\" latitude=\"42.83812\" longitude=\"-74.01260\" routeid=\"ML\" milepost=\"162.22\" type=\"Interchange\" exitid=\"26\" description=\"Schenectady - Scotia - I-890 - NY Routes 5 &amp; 5S\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m27x\" latitude=\"42.92295\" longitude=\"-74.19648\" routeid=\"ML\" milepost=\"173.59\" type=\"Interchange\" exitid=\"27\" description=\"Amsterdam - NY Route 30\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m28x\" latitude=\"42.94476\" longitude=\"-74.35976\" routeid=\"ML\" milepost=\"182.17\" type=\"Interchange\" exitid=\"28\" description=\"Fultonville - Fonda - NY Route 30A\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m29x\" latitude=\"42.90697\" longitude=\"-74.56224\" routeid=\"ML\" milepost=\"194.10\" type=\"Interchange\" exitid=\"29\" description=\"Canajoharie - Sharon Springs - NY Route 10\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m29a\" latitude=\"43.01657\" longitude=\"-74.81745\" routeid=\"ML\" milepost=\"210.62\" type=\"Interchange\" exitid=\"29A\" description=\"Little Falls - Dolgeville - NY Route 169\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m30x\" latitude=\"43.01778\" longitude=\"-74.98931\" routeid=\"ML\" milepost=\"219.70\" type=\"Interchange\" exitid=\"30\" description=\"Herkimer - Mohawk - NY Route 28\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m31x\" latitude=\"43.11198\" longitude=\"-75.20499\" routeid=\"ML\" milepost=\"232.85\" type=\"Interchange\" exitid=\"31\" description=\"Utica - I-790 - NY Routes 8 &amp; 12\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m32x\" latitude=\"43.12388\" longitude=\"-75.40102\" routeid=\"ML\" milepost=\"243.37\" type=\"Interchange\" exitid=\"32\" description=\"Westmoreland - Rome - NY Route 233\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m33x\" latitude=\"43.12375\" longitude=\"-75.58230\" routeid=\"ML\" milepost=\"252.71\" type=\"Interchange\" exitid=\"33\" description=\"Verona - Rome - Oneida - NY Route 365\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m34x\" latitude=\"43.09260\" longitude=\"-75.74720\" routeid=\"ML\" milepost=\"261.50\" type=\"Interchange\" exitid=\"34\" description=\"Canastota - Oneida - Chittenango - NY Route 13\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m34a\" latitude=\"43.09475\" longitude=\"-76.04353\" routeid=\"ML\" milepost=\"276.58\" type=\"Interchange\" exitid=\"34A\" description=\"Syracuse - Chittenango - Oswego - I-481\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m35x\" latitude=\"43.09278\" longitude=\"-76.08985\" routeid=\"ML\" milepost=\"278.93\" type=\"Interchange\" exitid=\"35\" description=\"Syracuse - East Syracuse - NY Route 298\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m36x\" latitude=\"43.09458\" longitude=\"-76.16788\" routeid=\"ML\" milepost=\"282.93\" type=\"Interchange\" exitid=\"36\" description=\"Watertown - Binghamton - I-81\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m37x\" latitude=\"43.10206\" longitude=\"-76.18139\" routeid=\"ML\" milepost=\"283.79\" type=\"Interchange\" exitid=\"37\" description=\"Syracuse - Liverpool - Electronics Parkway\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m38x\" latitude=\"43.11717\" longitude=\"-76.21867\" routeid=\"ML\" milepost=\"285.95\" type=\"Interchange\" exitid=\"38\" description=\"Syracuse - Liverpool - County Route 57\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m39x\" latitude=\"43.10730\" longitude=\"-76.28720\" routeid=\"ML\" milepost=\"289.53\" type=\"Interchange\" exitid=\"39\" description=\"Syracuse - Fulton - I-690 - NY Route 690\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m40x\" latitude=\"43.06112\" longitude=\"-76.56474\" routeid=\"ML\" milepost=\"304.19\" type=\"Interchange\" exitid=\"40\" description=\"Weedsport - Auburn - NY Route 34\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m41x\" latitude=\"42.96744\" longitude=\"-76.85294\" routeid=\"ML\" milepost=\"320.41\" type=\"Interchange\" exitid=\"41\" description=\"Waterloo - Clyde - NY Route 414\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m42x\" latitude=\"42.95489\" longitude=\"-76.98206\" routeid=\"ML\" milepost=\"327.10\" type=\"Interchange\" exitid=\"42\" description=\"Geneva - Lyons - NY Route 14\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m43x\" latitude=\"42.97874\" longitude=\"-77.23343\" routeid=\"ML\" milepost=\"340.15\" type=\"Interchange\" exitid=\"43\" description=\"Manchester - Palmyra - NY Route 21\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m44x\" latitude=\"42.99570\" longitude=\"-77.36840\" routeid=\"ML\" milepost=\"347.13\" type=\"Interchange\" exitid=\"44\" description=\"Canandaigua - Victor - NY Route 332\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m45x\" latitude=\"43.00922\" longitude=\"-77.43991\" routeid=\"ML\" milepost=\"350.99\" type=\"Interchange\" exitid=\"45\" description=\"Rochester - Victor - I-490\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m46x\" latitude=\"43.04775\" longitude=\"-77.65387\" routeid=\"ML\" milepost=\"362.44\" type=\"Interchange\" exitid=\"46\" description=\"Rochester - Corning - I-390\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m47x\" latitude=\"43.02896\" longitude=\"-77.96674\" routeid=\"ML\" milepost=\"378.56\" type=\"Interchange\" exitid=\"47\" description=\"Rochester - LeRoy - I-490 - NY Route 19\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m48x\" latitude=\"43.01859\" longitude=\"-78.18819\" routeid=\"ML\" milepost=\"390.13\" type=\"Interchange\" exitid=\"48\" description=\"Batavia - NY Route 98\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m48a\" latitude=\"43.00281\" longitude=\"-78.41385\" routeid=\"ML\" milepost=\"401.72\" type=\"Interchange\" exitid=\"48A\" description=\"Pembroke - Medina - NY Route 77\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m49x\" latitude=\"42.95065\" longitude=\"-78.70493\" routeid=\"ML\" milepost=\"417.27\" type=\"Interchange\" exitid=\"49\" description=\"Depew - Lockport - NY Route 78\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m50x\" latitude=\"42.94892\" longitude=\"-78.76393\" routeid=\"ML\" milepost=\"420.34\" type=\"Interchange\" exitid=\"50\" description=\"Niagara Falls - I-290\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m50a\" latitude=\"42.94412\" longitude=\"-78.76637\" routeid=\"ML\" milepost=\"420.93\" type=\"Interchange\" exitid=\"50A\" description=\"Cleveland Drive (No Westbound Exit)\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m51x\" latitude=\"42.93153\" longitude=\"-78.76629\" routeid=\"ML\" milepost=\"421.57\" type=\"Interchange\" exitid=\"51\" description=\"(51E &amp; 51W in Both Directions) Buffalo - NY Route 33 - Airport\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m52x\" latitude=\"42.90810\" longitude=\"-78.76672\" routeid=\"ML\" milepost=\"423.19\" type=\"Interchange\" exitid=\"52\" description=\"(52E &amp; 52W in Both Directions) Buffalo - Cheektowaga - Walden Avenue\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m52a\" latitude=\"42.88318\" longitude=\"-78.76863\" routeid=\"ML\" milepost=\"424.92\" type=\"Interchange\" exitid=\"52A\" description=\"Buffalo - William Street\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m53x\" latitude=\"42.87422\" longitude=\"-78.78891\" routeid=\"ML\" milepost=\"426.17\" type=\"Interchange\" exitid=\"53\" description=\"Buffalo (Downtown) - Canada - Niagara Falls - I-190\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m54x\" latitude=\"42.84910\" longitude=\"-78.79061\" routeid=\"ML\" milepost=\"427.94\" type=\"Interchange\" exitid=\"54\" description=\"West Seneca - East Aurora - NY Routes 400 &amp; 16\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m55x\" latitude=\"42.82744\" longitude=\"-78.79094\" routeid=\"ML\" milepost=\"429.47\" type=\"Interchange\" exitid=\"55\" description=\"Springville - Orchard Park - Lackawanna - West Seneca - US Route 219 - Ridge Road\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"m56x\" latitude=\"42.79274\" longitude=\"-78.81661\" routeid=\"ML\" milepost=\"432.45\" type=\"Interchange\" exitid=\"56\" description=\"Blasdell - Orchard Park - Mile Strip Road (NY Route 179)\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m57x\" latitude=\"42.74377\" longitude=\"-78.84037\" routeid=\"ML\" milepost=\"436.22\" type=\"Interchange\" exitid=\"57\" description=\"Hamburg - East Aurora - NY Route 75\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m57a\" latitude=\"42.65908\" longitude=\"-78.96090\" routeid=\"ML\" milepost=\"444.87\" type=\"Interchange\" exitid=\"57A\" description=\"Eden - Angola\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m58x\" latitude=\"42.55092\" longitude=\"-79.10812\" routeid=\"ML\" milepost=\"455.54\" type=\"Interchange\" exitid=\"58\" description=\"Silver Creek - Irving - NY Routes 5, 20 &amp; 438\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m59x\" latitude=\"42.46210\" longitude=\"-79.30580\" routeid=\"ML\" milepost=\"467.74\" type=\"Interchange\" exitid=\"59\" description=\"Dunkirk - Fredonia - NY Route 60\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m60x\" latitude=\"42.33869\" longitude=\"-79.58345\" routeid=\"ML\" milepost=\"485\" type=\"Interchange\" exitid=\"60\" description=\"Westfield - Mayville - NY Route 394\" route=\"I-90 - NYS Thruway\" tolled=\"true\" /> <interchange id=\"m61x\" latitude=\"42.26208\" longitude=\"-79.74435\" routeid=\"ML\" milepost=\"494.92\" type=\"Interchange\" exitid=\"61\" description=\"Ripley - Shortman Road\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"mpax\" latitude=\"42.25263\" longitude=\"-79.76175\" routeid=\"ML\" milepost=\"496.00\" type=\"Line\" exitid=\"\" description=\"Pennsylvania State Line (I-90)\" route=\"I-90 - NYS Thruway\" tolled=\"false\" /> <interchange id=\"ne00x\" latitude=\"40.85328\" longitude=\"-73.82634\" routeid=\"NE\" milepost=\"0.00\" type=\"Line\" exitid=\"\" description=\"Bruckner Expressway (I-95)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne08x\" latitude=\"40.85820\" longitude=\"-73.82670\" routeid=\"NE\" milepost=\"0.23\" type=\"Interchange\" exitid=\"8\" description=\"(8B Northbound; 8B &amp; 8C Southbound) City Island - Pelham Parkway - Orchard Beach\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne09x\" latitude=\"40.86045\" longitude=\"-73.82773\" routeid=\"NE\" milepost=\"0.50\" type=\"Interchange\" exitid=\"9\" description=\"Hutchinson River Parkway North\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne10x\" latitude=\"40.86229\" longitude=\"-73.82929\" routeid=\"NE\" milepost=\"0.55\" type=\"Interchange\" exitid=\"10\" description=\"Gun Hill Road (No Southbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne11x\" latitude=\"40.86851\" longitude=\"-73.83266\" routeid=\"NE\" milepost=\"1.11\" type=\"Interchange\" exitid=\"11\" description=\"Bartow Avenue - Co-op City Boulevard\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne12x\" latitude=\"40.87562\" longitude=\"-73.83453\" routeid=\"NE\" milepost=\"1.61\" type=\"Interchange\" exitid=\"12\" description=\"Baychester Avenue (No Southbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne13x\" latitude=\"40.88389\" longitude=\"-73.82580\" routeid=\"NE\" milepost=\"2.37\" type=\"Interchange\" exitid=\"13\" description=\"Conner Street - Baychester Avenue\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne14x\" latitude=\"40.88670\" longitude=\"-73.81524\" routeid=\"NE\" milepost=\"2.96\" type=\"Interchange\" exitid=\"14\" description=\"Hutchinson River Parkway South - Whitestone Bridge (No Northbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne15x\" latitude=\"40.90039\" longitude=\"-73.79564\" routeid=\"NE\" milepost=\"4.63\" type=\"Interchange\" exitid=\"15\" description=\"New Rochelle - The Pelhams – US Route 1\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne16x\" latitude=\"40.91707\" longitude=\"-73.78120\" routeid=\"NE\" milepost=\"6.04\" type=\"Interchange\" exitid=\"16\" description=\"New Rochelle - North Avenue - Cedar Street\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne17x\" latitude=\"40.92955\" longitude=\"-73.76550\" routeid=\"NE\" milepost=\"7.01\" type=\"Interchange\" exitid=\"17\" description=\"Larchmont - Chatsworth Avenue (No Southbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne18a\" latitude=\"40.95185\" longitude=\"-73.74556\" routeid=\"NE\" milepost=\"9.14\" type=\"Interchange\" exitid=\"18A\" description=\"Mamaroneck - Fenimore Road (No Southbound Exit) - Mamaroneck - Mamaroneck Avenue East (No Northbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne18b\" latitude=\"40.96228\" longitude=\"-73.73532\" routeid=\"NE\" milepost=\"10.04\" type=\"Interchange\" exitid=\"18B\" description=\"White Plains - Mamaroneck Avenue\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne19x\" latitude=\"40.97686\" longitude=\"-73.69899\" routeid=\"NE\" milepost=\"12.48\" type=\"Interchange\" exitid=\"19\" description=\"Rye - Harrison - Playland Parkway\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne20x\" latitude=\"40.98740\" longitude=\"-73.68029\" routeid=\"NE\" milepost=\"13.39\" type=\"Interchange\" exitid=\"20\" description=\"Rye - US Route 1 South (No Southbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne21x\" latitude=\"40.98778\" longitude=\"-73.67749\" routeid=\"NE\" milepost=\"13.91\" type=\"Interchange\" exitid=\"21\" description=\"White Plains - Port Chester - Cross Westchester Expressway (I-287) - US Route 1 North - Governor Mario M. Cuomo Bridge\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"ne22x\" latitude=\"40.98750\" longitude=\"-73.67277\" routeid=\"NE\" milepost=\"14.03\" type=\"Interchange\" exitid=\"22\" description=\"Port Chester - Rye - Midland Avenue (No Southbound Exit)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"nectx\" latitude=\"40.99548\" longitude=\"-73.65838\" routeid=\"NE\" milepost=\"15.01\" type=\"Line\" exitid=\"\" description=\"Connecticut State Line - Connecticut Turnpike (I-95)\" route=\"I-95 - New England Thruway\" tolled=\"false\" /> <interchange id=\"cw01x\" latitude=\"41.05902\" longitude=\"-73.82964\" routeid=\"CW\" milepost=\"0.17\" type=\"Interchange\" exitid=\"1\" description=\"Tarrytown - Saw Mill River Parkway - NY Route 119 (No Eastbound Exit)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw02x\" latitude=\"41.05815\" longitude=\"-73.81714\" routeid=\"CW\" milepost=\"0.80\" type=\"Interchange\" exitid=\"2\" description=\"Elmsford - Saw Mill River Road (NY Route 9A) (No Eastbound Exit)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw03x\" latitude=\"41.05500\" longitude=\"-73.81026\" routeid=\"CW\" milepost=\"1.22\" type=\"Interchange\" exitid=\"3\" description=\"New York City - Taconic State Parkway - Sprain Brook Parkway\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw04x\" latitude=\"41.04720\" longitude=\"-73.80384\" routeid=\"CW\" milepost=\"1.87\" type=\"Interchange\" exitid=\"4\" description=\"Hartsdale - Knollwood Road (NY Route 100A)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw05x\" latitude=\"41.04468\" longitude=\"-73.79043\" routeid=\"CW\" milepost=\"2.60\" type=\"Interchange\" exitid=\"5\" description=\"White Plains - Hillside Avenue (NY Route 100) - Tarrytown-White Plains Road (NY Route 119)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw06x\" latitude=\"41.04579\" longitude=\"-73.76950\" routeid=\"CW\" milepost=\"3.78\" type=\"Interchange\" exitid=\"6\" description=\"White Plains - North White Plains - Broadway (NY Route 22)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw07x\" latitude=\"41.04456\" longitude=\"-73.76461\" routeid=\"CW\" milepost=\"4.06\" type=\"Interchange\" exitid=\"7\" description=\"North White Plains - Central Westchester Parkway North - Taconic State Parkway (No Eastbound Exit)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw08x\" latitude=\"41.03383\" longitude=\"-73.75378\" routeid=\"CW\" milepost=\"5.09\" type=\"Interchange\" exitid=\"8\" description=\"(8 Westbound; 8E &amp; 8W Eastbound) White Plains - NY Route 127 – Westchester Avenue (NY Route 119) - Westchester Mall Place - Bloomingdale Road\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw09a\" latitude=\"41.02472\" longitude=\"-73.73036\" routeid=\"CW\" milepost=\"6.45\" type=\"Interchange\" exitid=\"9A\" description=\"Brewster - I-684 - Westchester Avenue\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw09x\" latitude=\"41.01410\" longitude=\"-73.71700\" routeid=\"CW\" milepost=\"7.55\" type=\"Interchange\" exitid=\"9\" description=\"Hutchinson River Parkway - Merritt Parkway - Whitestone Bridge\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw10x\" latitude=\"41.00841\" longitude=\"-73.69776\" routeid=\"CW\" milepost=\"8.70\" type=\"Interchange\" exitid=\"10\" description=\"Purchase - Port Chester - NY Routes 120 &amp; 120A - Bowman Avenue - Webb Avenue\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw11x\" latitude=\"40.99357\" longitude=\"-73.67815\" routeid=\"CW\" milepost=\"10.27\" type=\"Interchange\" exitid=\"11\" description=\"Port Chester - Rye - US Route 1 (No Westbound Exit)\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"cw12x\" latitude=\"40.99193\" longitude=\"-73.67729\" routeid=\"CW\" milepost=\"10.39\" type=\"Interchange\" exitid=\"12\" description=\"New York - I-95 South\" route=\"I-287 - Cross Westchester Expressway\" tolled=\"false\" /> <interchange id=\"b1x\" latitude=\"42.48210\" longitude=\"-73.67022\" routeid=\"B\" milepost=\"6.58\" type=\"Interchange\" exitid=\"B1\" description=\"Albany - Hudson - Rensselaer - I-90 - US Route 9\" route=\"Berkshire Connector\" tolled=\"true\" /> <interchange id=\"b2x\" latitude=\"42.42027\" longitude=\"-73.54834\" routeid=\"B\" milepost=\"15.09\" type=\"Interchange\" exitid=\"B2\" description=\"Taconic State Parkway - NY Route 295\" route=\"Berkshire Connector\" tolled=\"true\" /> <interchange id=\"b3x\" latitude=\"42.35767\" longitude=\"-73.42707\" routeid=\"B\" milepost=\"23.27\" type=\"Interchange\" exitid=\"B3\" description=\"Austerlitz - New Lebanon - NY Route 22\" route=\"Berkshire Connector\" tolled=\"false\" /> <interchange id=\"bxx\" latitude=\"42.34758\" longitude=\"-73.41155\" routeid=\"B\" milepost=\"24.28\" type=\"Line\" exitid=\"\" description=\"Massachusetts State Line - Massachusetts Turnpike (I-90)\" route=\"Berkshire Connector\" tolled=\"false\" /> <interchange id=\"ns01x\" latitude=\"42.87632\" longitude=\"-78.80161\" routeid=\"N\" milepost=\"0.70\" type=\"Interchange\" exitid=\"1\" description=\"South Ogden Street - Dingens Street\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns02x\" latitude=\"42.87197\" longitude=\"-78.81649\" routeid=\"N\" milepost=\"1.40\" type=\"Interchange\" exitid=\"2\" description=\"Bailey Avenue (US Route 62) - Clinton Street (NY Route 354)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns03x\" latitude=\"42.86801\" longitude=\"-78.82775\" routeid=\"N\" milepost=\"2.41\" type=\"Interchange\" exitid=\"3\" description=\"Seneca Street (NY Route 16) (No Northbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns04x\" latitude=\"42.87089\" longitude=\"-78.84536\" routeid=\"N\" milepost=\"3.14\" type=\"Interchange\" exitid=\"4\" description=\"Smith Street - Fillmore Avenue\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns05x\" latitude=\"42.87339\" longitude=\"-78.85764\" routeid=\"N\" milepost=\"3.95\" type=\"Interchange\" exitid=\"5\" description=\"Hamburg Street - Louisiana Street\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns06x\" latitude=\"42.87715\" longitude=\"-78.87169\" routeid=\"N\" milepost=\"4.65\" type=\"Interchange\" exitid=\"6\" description=\"Elm Street\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns07x\" latitude=\"42.88129\" longitude=\"-78.88052\" routeid=\"N\" milepost=\"5.20\" type=\"Interchange\" exitid=\"7\" description=\"Lackawanna - Buffalo Skyway (NY Route 5 West) - Church Street\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns08x\" latitude=\"42.89008\" longitude=\"-78.88949\" routeid=\"N\" milepost=\"5.86\" type=\"Interchange\" exitid=\"8\" description=\"Niagara Street (NY Route 266)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns09x\" latitude=\"42.89847\" longitude=\"-78.89786\" routeid=\"N\" milepost=\"6.58\" type=\"Interchange\" exitid=\"9\" description=\"Fort Erie Canada - Porter Avenue - Peace Bridge\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns11x\" latitude=\"42.92820\" longitude=\"-78.89862\" routeid=\"N\" milepost=\"8.69\" type=\"Interchange\" exitid=\"11\" description=\"Scajaquada Expressway (NY Route 198)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns12x\" latitude=\"42.93391\" longitude=\"-78.90299\" routeid=\"N\" milepost=\"9.06\" type=\"Interchange\" exitid=\"12\" description=\"Amherst Street (No Southbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns13x\" latitude=\"42.93782\" longitude=\"-78.90507\" routeid=\"N\" milepost=\"9.27\" type=\"Interchange\" exitid=\"13\" description=\"Austin Street (No Southbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns14x\" latitude=\"42.94380\" longitude=\"-78.90802\" routeid=\"N\" milepost=\"10.27\" type=\"Interchange\" exitid=\"14\" description=\"Ontario Street - Vulcan Street\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns15x\" latitude=\"42.97368\" longitude=\"-78.91174\" routeid=\"N\" milepost=\"12.36\" type=\"Interchange\" exitid=\"15\" description=\"NY Route 324 - Sheridan Drive - Kenmore Avenue\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns16x\" latitude=\"42.98669\" longitude=\"-78.91446\" routeid=\"N\" milepost=\"13.33\" type=\"Interchange\" exitid=\"16\" description=\"Tonawanda - Rochester - Youngmann Memorial Highway (I-290 East) - I-190\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns17x\" latitude=\"42.99377\" longitude=\"-78.92849\" routeid=\"N\" milepost=\"14.20\" type=\"Interchange\" exitid=\"17\" description=\"Tonawandas - River Road (NY Route 266)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns18a\" latitude=\"43.00369\" longitude=\"-78.9455\" routeid=\"N\" milepost=\"15.35\" type=\"Interchange\" exitid=\"18A\" description=\"Grand Island Boulevard (NY Route 324 West) (No Southbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns18b\" latitude=\"43.00562\" longitude=\"-78.94961\" routeid=\"N\" milepost=\"15.46\" type=\"Interchange\" exitid=\"18B\" description=\"Beaver Island State Park (No Southbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns18x\" latitude=\"43.00609\" longitude=\"-78.95100\" routeid=\"N\" milepost=\"15.61\" type=\"Interchange\" exitid=\"18\" description=\"Beaver Island State Park (No Northbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns19x\" latitude=\"43.02140\" longitude=\"-78.97432\" routeid=\"N\" milepost=\"17.27\" type=\"Interchange\" exitid=\"19\" description=\"Whitehaven Road\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns20x\" latitude=\"43.04840\" longitude=\"-78.99046\" routeid=\"N\" milepost=\"18.83\" type=\"Interchange\" exitid=\"20\" description=\"Long Road (No Southbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns20b\" latitude=\"43.04873\" longitude=\"-78.99056\" routeid=\"N\" milepost=\"19.32\" type=\"Interchange\" exitid=\"20B\" description=\"Grand Island Boulevard (NY Route 324) - Long Road (No Northbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns20a\" latitude=\"43.05369\" longitude=\"-78.99054\" routeid=\"N\" milepost=\"19.69\" type=\"Interchange\" exitid=\"20A\" description=\"West River Parkway (No Northbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"ns21x\" latitude=\"43.07412\" longitude=\"-78.99077\" routeid=\"N\" milepost=\"21.07\" type=\"Interchange\" exitid=\"21\" description=\"Niagara Scenic Parkway - Buffalo Avenue (NY Route 384) (No Southbound Exit)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> <interchange id=\"nsnex\" latitude=\"43.07572\" longitude=\"-78.99078\" routeid=\"N\" milepost=\"21.24\" type=\"Line\" exitid=\"\" description=\"Niagara Expressway (I-190)\" route=\"I-190 - Niagara Thruway\" tolled=\"false\" /> </interchanges>";
#endregion
    // Радиусы поиска в метрах (как в ParsePaTurnpikeInterchangesCommand)
    private static readonly double[] SearchRadiiMeters = [ 200];

    public async Task<ParseMassachusettsInterchangesResult> Handle(
        ParseNewYorkInterchangesCommand request,
        CancellationToken ct)
    {
        var errors = new List<string>();
        int processed = 0;
        int created = 0;
        int updated = 0;

       

        //if (string.IsNullOrWhiteSpace(request.HtmlFragment))
        //{
        //    return new ParseMassachusettsInterchangesResult(0, 0, 0, new() { "HtmlFragment is empty" });
        //}

        XDocument doc;
        try
        {
            // Фрагмент уже должен быть корректным XML: <interchanges>...</interchanges>
            doc = XDocument.Parse(htmlCode, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            return new ParseMassachusettsInterchangesResult(
                0, 0, 0,
                new() { $"Ошибка парсинга XML: {ex.Message}" });
        }

        var interchanges = doc.Root?.Name.LocalName.Equals("interchanges", StringComparison.OrdinalIgnoreCase) == true
            ? doc.Root.Elements("interchange")
            : doc.Descendants("interchange");

        foreach (var interchange in interchanges)
        {
            //ct.ThrowIfCancellationRequested();
            //processed++;

            try
            {
                var tolledAttr = (string?)interchange.Attribute("tolled");
                var isTolled = tolledAttr != null &&
                               tolledAttr.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (!isTolled)
                {
                    continue;
                }

                var latStr = (string?)interchange.Attribute("latitude");
                var lonStr = (string?)interchange.Attribute("longitude");
                if (string.IsNullOrWhiteSpace(latStr) || string.IsNullOrWhiteSpace(lonStr))
                {
                    errors.Add("Interchange without coordinates was skipped");
                    continue;
                }

                if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
                {
                    errors.Add($"Invalid coordinates: lat='{latStr}', lon='{lonStr}'");
                    continue;
                }

                var exitId = (string?)interchange.Attribute("exitid");
                var description = (string?)interchange.Attribute("description") ?? string.Empty;

                var tollPoint = new Point(lon, lat) { SRID = 4326 };

                // Пытаемся найти до двух ближайших существующих Toll'ов
                var matchingTolls = await FindClosestTollsAsync(context, lat, lon, ct, maxCount: 2);

                if (matchingTolls.Count == 0)
                {
                    // Создаем новый Toll
                    var newToll = new Toll
                    {
                        Id = Guid.NewGuid(),
                        Name = description,
                        Number = string.IsNullOrWhiteSpace(exitId) ? null : exitId,
                        Location = tollPoint,
                        Price = 0,
                        Key = description,
                        isDynamic = false
                    };

                    context.Tolls.Add(newToll);
                    created++;
                }
                else
                {
                    // Обновляем до двух найденных точек
                    foreach (var matchingToll in matchingTolls)
                    {
                        if(matchingToll.Key == "Schenectady - Scotia - I-890 - NY Routes 5 & 5S")
                        {

                        }

                        if (matchingToll.Key != null && !string.IsNullOrEmpty(matchingToll.Key))
                            continue;

                        var changed = false;

                        if (!string.IsNullOrWhiteSpace(description) &&
                            !string.Equals(matchingToll.Name, description, StringComparison.Ordinal))
                        {
                            matchingToll.Name = description;
                            changed = true;
                        }

                        if (!string.IsNullOrWhiteSpace(exitId) &&
                            matchingToll.Number != exitId)
                        {
                            matchingToll.Number = exitId;
                            changed = true;
                        }

                        if (matchingToll.Key != description)
                        {
                            matchingToll.Key = description;
                            changed = true;
                        }

                        if (changed)
                        {
                            updated++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Ошибка при обработке interchange: {ex.Message}");
            }
        }

        if (created > 0 || updated > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        return new ParseMassachusettsInterchangesResult(processed, created, updated, errors);
    }

    /// <summary>
    /// Находит до maxCount ближайших Toll'ов к заданной точке,
    /// постепенно увеличивая радиус поиска.
    /// </summary>
    private static async Task<List<Toll>> FindClosestTollsAsync(
        ITollDbContext context,
        double latitude,
        double longitude,
        CancellationToken ct,
        int maxCount = 2)
    {
        var point = new Point(longitude, latitude) { SRID = 4326 };
        var result = new List<Toll>();

        // Как и в ParsePaTurnpikeInterchangesCommand: сначала маленький радиус, потом увеличиваем
        foreach (var radius in SearchRadiiMeters)
        {
            var tolls = await context.Tolls
                .Where(t => t.Location != null && t.Location.IsWithinDistance(point, radius))
                .OrderBy(t => t.Location!.Distance(point))
                .Take(maxCount)
                .ToListAsync(ct);

            if (tolls.Count > 0)
            {
                result.AddRange(tolls);
                // Ограничиваемся maxCount результатов
                if (result.Count >= maxCount)
                {
                    return result.Take(maxCount).ToList();
                }
            }
        }

        return result;
    }
}


