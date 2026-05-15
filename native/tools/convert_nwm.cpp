#include "Novus/DataModel.h"
#include "Novus/Application.h"

#include <iostream>

using namespace Novus;

int main(int argc, char** argv) {
  if (argc < 3) {
    std::cerr << "Usage: novus-nwm-convert <input.nwm/json> <output.nwm>\n";
    return 1;
  }

  try {
    const std::string input = ReadTextFile(argv[1]);
    if (input.empty()) {
      std::cerr << "Input file is empty or missing.\n";
      return 2;
    }
    const Json json = Json::Parse(input);
    const DataModel model = DataModel::FromLegacyPlaceJson(json);
    if (!WriteTextFile(argv[2], model.ToJson().Dump(2))) {
      std::cerr << "Could not write output.\n";
      return 3;
    }
    std::cout << "Converted to NovusDataModel: " << argv[2] << "\n";
    return 0;
  } catch (const std::exception& err) {
    std::cerr << err.what() << "\n";
    return 4;
  }
}
